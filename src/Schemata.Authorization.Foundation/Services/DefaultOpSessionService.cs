using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Skeleton.Services;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Services;

/// <summary>
///     The OP session authority, per
///     <seealso href="https://openid.net/specs/openid-connect-rpinitiated-1_0.html">
///         OpenID Connect RP-Initiated Logout 1.0 §2: Logout Request
///     </seealso>
///     . Issuance resolves one canonical session identifier from collected evidence: a
///     verified host ticket (the principal's sid claim) outranks every other source, trusted
///     sources that disagree fail closed instead of picking a winner, an untrusted browser
///     mirror is reused only when no trusted evidence exists (and is reconciled to the trusted
///     identity on persist), and a fresh grant-scoped identifier is minted only under an
///     authenticated issuance basis. Persist and clear run every applicable adapter in
///     ascending order; persist failures propagate, clear failures are aggregated.
/// </summary>
public sealed class DefaultOpSessionService(
    IOptions<SchemataAuthorizationOptions> authorization,
    IEnumerable<IOpSessionStore>?          stores = null,
    Schemata.Security.Skeleton.Services.ITokenStore<Schemata.Security.Skeleton.Entities.SchemataToken>? tokens = null,
    TimeProvider? time = null
) : IOpSessionService
{
    private readonly IOpSessionStore[] _stores = (stores ?? []).OrderBy(s => s.Order).ToArray();
    private const string OnlineProvider = "op-session";
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    #region IOpSessionService Members

    public async Task<string?> IssueAsync(ClaimsPrincipal? principal, string? subject, CancellationToken ct = default) {
        var sid = await ResolveCanonicalAsync(principal, subject, ct);
        if (sid is null) {
            // A fresh grant-scoped identifier is minted only under an authenticated issuance
            // basis: an anonymous OAuth request — raw auxiliary cookie or synthetic bearer
            // principal alike — never establishes an OP session.
            if (!HasHostAuthenticationBasis(principal)) {
                return null;
            }

            sid = Mint();
        }

        foreach (var store in _stores) {
            await store.PersistAsync(sid, principal, subject, ct);
        }
        return sid;
    }

    public async Task<string?> ResolveAsync(ClaimsPrincipal? principal, string? subject, CancellationToken ct = default) {
        return await ResolveCanonicalAsync(principal, subject, ct);
    }

    public async Task<LogoutSessionEvidence?> ResolveLogoutAsync(
        LogoutSessionTarget target, ClaimsPrincipal? principal, CancellationToken ct = default) {
        if (string.IsNullOrWhiteSpace(target.Subject) || string.IsNullOrWhiteSpace(target.Application)) return null;
        LogoutSessionEvidence? result = null;
        foreach (var store in _stores) {
            if (await store.ReadLogoutAsync(target, principal, ct) is not { } evidence) continue;
            if (!string.Equals(evidence.Target.Subject, target.Subject, StringComparison.Ordinal)
                || !string.Equals(evidence.Target.Application, target.Application, StringComparison.Ordinal)
                || target.SessionId is not null
                   && !string.Equals(evidence.Target.SessionId, target.SessionId, StringComparison.Ordinal)
                || result is not null && result.Target != evidence.Target) {
                throw new InvalidOperationException("Logout session evidence disagrees on the canonical target.");
            }
            result = new(evidence.Target,
                evidence.MatchesCurrent || result?.MatchesCurrent == true,
                evidence.HasRecentEvidence || result?.HasRecentEvidence == true);
        }
        var sid = target.SessionId ?? result?.Target.SessionId;
        var host = HasHostAuthenticationBasis(principal)
            && !string.Equals(principal?.Identity?.AuthenticationType, SchemataAuthorizationSchemes.Code, StringComparison.Ordinal)
            && string.Equals(principal?.FindFirstValue(Schemata.Abstractions.SchemataConstants.IdentityClaims.Subject),
                target.Subject, StringComparison.Ordinal);
        var hostSid = host ? principal?.FindFirstValue(authorization.Value.SessionIdClaimType) : null;
        sid ??= hostSid;
        if (tokens is null || string.IsNullOrWhiteSpace(sid)) return result;
        var slot = await tokens.GetAsync(target.Subject, OnlineProvider, sid, ct);
        var online = slot is not null && (slot.ExpireTime is null || slot.ExpireTime > _time.GetUtcNow().UtcDateTime);
        var associated = false;
        await foreach (var token in tokens.ListBySessionAsync(sid, ct)) {
            if (token.Type is Schemata.Authorization.Skeleton.AuthorizationConstants.TokenTypes.AccessToken
                    or Schemata.Authorization.Skeleton.AuthorizationConstants.TokenTypes.AuthorizationCode
                    or Schemata.Authorization.Skeleton.AuthorizationConstants.TokenTypes.RefreshToken
                && token.Status == Schemata.Authorization.Skeleton.AuthorizationConstants.TokenStatuses.Valid
                && string.Equals(token.Parent, target.Subject, StringComparison.Ordinal)
                && string.Equals(token.Application, target.Application, StringComparison.Ordinal)
                && (token.ExpireTime is null || token.ExpireTime > _time.GetUtcNow().UtcDateTime)) {
                var grant = AuthorizationGrantContexts.Deserialize(token.GrantContext);
                if (grant?.NativeSessionKind == NativeSessionKinds.Online
                    && (!online || string.IsNullOrWhiteSpace(grant.OnlineSessionAuthority)
                        || !string.Equals(slot!.Value, grant.OnlineSessionAuthority, StringComparison.Ordinal))) continue;
                associated = true;
                break;
            }
        }
        if (!associated) return result;
        var current = host && string.Equals(hostSid, sid, StringComparison.Ordinal);
        if (!current && !online) return result;
        var resolved = target with { SessionId = sid };
        if (result is not null && result.Target != resolved) {
            throw new InvalidOperationException("Logout session evidence disagrees on the canonical target.");
        }
        return new(resolved, current || result?.MatchesCurrent == true, online || result?.HasRecentEvidence == true);
    }

    public async Task InvalidateAsync(ClaimsPrincipal? principal, string? subject, string? sessionId, CancellationToken ct = default) {
        var target = sessionId;
        if (string.IsNullOrWhiteSpace(target)) {
            target = await ResolveCanonicalAsync(principal, subject, ct);
        }

        // The absence of a session identifier is not the absence of a session: adapters that
        // own a subject-scoped host session still clear it, receiving a null target.
        var failures = new List<Exception>();
        foreach (var store in _stores) {
            try {
                await store.ClearAsync(string.IsNullOrWhiteSpace(target) ? null : target, principal, subject, ct);
            } catch (OperationCanceledException) {
                throw;
            } catch (Exception ex) {
                failures.Add(ex);
            }
        }
        if (tokens is not null && !string.IsNullOrWhiteSpace(subject) && !string.IsNullOrWhiteSpace(target)) {
            try {
                await tokens.RemoveAsync(subject, OnlineProvider, target, ct);
            } catch (OperationCanceledException) {
                throw;
            } catch (Exception ex) {
                failures.Add(ex);
            }
        }

        if (failures.Count > 0) {
            throw new AggregateException(failures);
        }
    }

    public async Task<string?> EstablishOnlineAsync(string? subject, string? sessionId, CancellationToken ct = default) {
        if (tokens is null || string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(sessionId)) {
            return null;
        }

        var slot = await tokens.GetOrCreateAsync(
            subject, OnlineProvider, sessionId, null, authorization.Value.OnlineSessionLifetime, ct);
        return slot.Value;
    }

    public async Task<bool> ValidateOnlineAsync(
        string? subject, string? sessionId, string? generation, CancellationToken ct = default) {
        if (tokens is null
            || string.IsNullOrWhiteSpace(subject)
            || string.IsNullOrWhiteSpace(sessionId)
            || string.IsNullOrWhiteSpace(generation)) {
            return false;
        }

        var slot = await tokens.GetAsync(subject, OnlineProvider, sessionId, ct);
        // A slot without ExpireTime lives under store-managed eviction (cache backends), so its
        // presence alone proves it unexpired.
        return slot is not null
            && string.Equals(slot.Value, generation, StringComparison.Ordinal)
            && (slot.ExpireTime is null || slot.ExpireTime > _time.GetUtcNow().UtcDateTime);
    }

    #endregion

    private async Task<string?> ResolveCanonicalAsync(ClaimsPrincipal? principal, string? subject, CancellationToken ct) {
        var trusted = new List<OpSessionEvidence> {
            // The principal's sid claim is the verified host ticket the user actually signed in to.
            new(subject, principal?.FindFirstValue(authorization.Value.SessionIdClaimType), OpSessionProvenance.HostTicket),
        };

        var mirrorEvidence = new List<OpSessionEvidence>();
        foreach (var store in _stores) {
            if (await store.ReadAsync(principal, subject, ct) is not { } evidence) {
                continue;
            }

            if (evidence.Provenance is OpSessionProvenance.BrowserMirror) {
                if (!string.IsNullOrWhiteSpace(evidence.SessionId)) {
                    mirrorEvidence.Add(evidence);
                }

                continue;
            }

            trusted.Add(evidence);
        }

        var trustedSessions = trusted.Where(e => !string.IsNullOrWhiteSpace(e.SessionId))
                                     .Select(e => e.SessionId)
                                     .Distinct(StringComparer.Ordinal)
                                     .ToList();
        if (trustedSessions.Count > 1) {
            throw new InvalidOperationException(
                $"Trusted OP session evidence disagrees on the session identifier: {string.Join(", ", trustedSessions)}. "
                + "Resolve the conflict explicitly or reject the request.");
        }

        // Trusted evidence may also name the subject; a caller subject that contradicts it is
        // a conflict, not a merge — one session cannot carry another subject's identifier.
        var trustedSubjects = trusted.Where(e => !string.IsNullOrWhiteSpace(e.Subject))
                                     .Select(e => e.Subject)
                                     .Distinct(StringComparer.Ordinal)
                                     .ToList();
        if (trustedSubjects.Count > 1
         || (trustedSubjects.Count == 1
             && !string.IsNullOrWhiteSpace(subject)
             && !string.Equals(trustedSubjects[0], subject, StringComparison.Ordinal))) {
            throw new InvalidOperationException(
                $"Trusted OP session evidence disagrees on the subject: caller '{subject}', evidence '{string.Join(", ", trustedSubjects)}'. "
                + "Resolve the conflict explicitly or reject the request.");
        }

        if (trustedSessions.Count == 1) {
            return trustedSessions[0];
        }

        // No trusted evidence: the untrusted browser mirror is the only established channel,
        // and only when it belongs to the same subject — a mirror left by account A is not
        // account B's session, so an account switch mints a fresh observable session instead
        // of silently continuing the previous account's. Divergent same-subject mirrors are
        // genuinely ambiguous — no registration order picks a winner.
        var boundMirrors = !HasHostAuthenticationBasis(principal)
            ? []
            : mirrorEvidence
              .Where(e => SubjectsMatch(e.Subject, subject))
              .Select(e => e.SessionId)
              .ToList();
        var distinctMirrors = boundMirrors.Distinct(StringComparer.Ordinal).ToList();
        if (distinctMirrors.Count > 1) {
            throw new InvalidOperationException(
                $"Browser session mirrors disagree on the session identifier: {string.Join(", ", distinctMirrors)}. "
                + "Resolve the conflict explicitly or reject the request.");
        }

        return distinctMirrors.Count == 1 ? distinctMirrors[0] : null;
    }

    /// <summary>
    ///     Whether the principal carries trustworthy host authentication: authenticated, and not
    ///     one of this OP's own token schemes — a grant principal synthesized for token issuance
    ///     (the authorization Bearer scheme) is an OP artifact, never login evidence.
    /// </summary>
    private static bool HasHostAuthenticationBasis(ClaimsPrincipal? principal) {
        return principal?.Identity is ClaimsIdentity { IsAuthenticated: true } identity
            && !string.Equals(identity.AuthenticationType, SchemataAuthorizationSchemes.Bearer, StringComparison.Ordinal);
    }

    private static bool SubjectsMatch(string? evidenceSubject, string? callerSubject) {
        return string.Equals(evidenceSubject ?? string.Empty, callerSubject ?? string.Empty, StringComparison.Ordinal);
    }

    private static string Mint() {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
    }
}
