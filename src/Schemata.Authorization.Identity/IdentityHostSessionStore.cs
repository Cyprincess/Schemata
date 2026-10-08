using System;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using Schemata.Authorization.Skeleton.Services;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Identity;

/// <summary>
///     Host-ticket lifecycle adapter for ASP.NET Core Identity. RP association is read from
///     live credentials owned by the authenticated subject and exact session. Clearing that
///     target signs out the application cookie scheme.
/// </summary>
public sealed class IdentityHostSessionStore(
    IHttpContextAccessor                   accessor,
    IOptions<SchemataAuthorizationOptions> options,
    ITokenStore<SchemataToken> tokens,
    TimeProvider? time = null
) : IOpSessionStore
{
    /// <summary>Runs before browser channels so host sign-out precedes browser-state rotation.</summary>
    public const int DefaultOrder = 0;

    #region IOpSessionStore Members

    public int Order => DefaultOrder;

    public Task<OpSessionEvidence?> ReadAsync(ClaimsPrincipal? principal, string? subject, CancellationToken ct = default) {
        return Task.FromResult<OpSessionEvidence?>(null);
    }

    public async Task<LogoutSessionEvidence?> ReadLogoutAsync(
        LogoutSessionTarget target, ClaimsPrincipal? principal, CancellationToken ct = default) {
        if (accessor.HttpContext is not { } http) return null;
        var owned = await http.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        if (!owned.Succeeded || owned.Principal is null) return null;
        var subject = owned.Principal.FindFirstValue(Abstractions.SchemataConstants.IdentityClaims.Subject);
        var sid = owned.Principal.FindFirstValue(options.Value.SessionIdClaimType);
        if (string.IsNullOrWhiteSpace(sid)
            || !string.Equals(subject, target.Subject, StringComparison.Ordinal)
            || target.SessionId is not null && !string.Equals(sid, target.SessionId, StringComparison.Ordinal)) return null;
        var now = (time ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        var online = await tokens.GetAsync(subject, "op-session", sid, ct);
        await foreach (var token in tokens.ListBySessionAsync(sid, ct)) {
            if (token.Type is Skeleton.AuthorizationConstants.TokenTypes.AccessToken
                    or Skeleton.AuthorizationConstants.TokenTypes.AuthorizationCode
                    or Skeleton.AuthorizationConstants.TokenTypes.RefreshToken
                && token.Status == Skeleton.AuthorizationConstants.TokenStatuses.Valid
                && string.Equals(token.Parent, subject, StringComparison.Ordinal)
                && string.Equals(token.Application, target.Application, StringComparison.Ordinal)
                && (token.ExpireTime is null || token.ExpireTime > now)) {
                var grant = string.IsNullOrWhiteSpace(token.GrantContext) ? null
                    : JsonSerializer.Deserialize<AuthorizationGrantContext>(token.GrantContext, Common.SchemataJson.Default);
                if (grant?.NativeSessionKind == NativeSessionKinds.Online) {
                    if (online is null || string.IsNullOrWhiteSpace(grant.OnlineSessionAuthority)
                        || !string.Equals(online.Value, grant.OnlineSessionAuthority, StringComparison.Ordinal)
                        || online.ExpireTime is { } expiry && expiry <= now) continue;
                }
                return new(target with { SessionId = sid }, true, false);
            }
        }
        return null;
    }

    public Task PersistAsync(string sessionId, ClaimsPrincipal? principal, string? subject, CancellationToken ct = default) {
        return Task.CompletedTask;
    }
    public async Task ClearAsync(string? sessionId, ClaimsPrincipal? principal, string? subject, CancellationToken ct = default) {
        if (accessor.HttpContext is not { } http) return;
        var owned = await http.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        if (!owned.Succeeded || owned.Principal is null) return;
        var ownedSubject = owned.Principal.FindFirstValue(Abstractions.SchemataConstants.IdentityClaims.Subject);
        var ownedSession = owned.Principal.FindFirstValue(options.Value.SessionIdClaimType);
        if (string.IsNullOrWhiteSpace(ownedSession) && http.RequestServices.GetService<IOpSessionService>() is { } authority) {
            ownedSession = await authority.ResolveAsync(owned.Principal, ownedSubject, ct);
        }
        if (!string.Equals(ownedSubject, subject, StringComparison.Ordinal)) return;
        if (!string.IsNullOrWhiteSpace(sessionId)
            && !string.Equals(ownedSession, sessionId, StringComparison.Ordinal)) return;
        await http.SignOutAsync(IdentityConstants.ApplicationScheme);
    }

    #endregion
}
