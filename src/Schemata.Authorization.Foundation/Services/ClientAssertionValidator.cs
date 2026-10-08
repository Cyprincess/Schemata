using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Authorization.Foundation.Authentication;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Security.Skeleton;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Services;

/// <summary>
///     Validates JWT client assertions per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc7523.html#section-3">
///         RFC 7523: JWT Profile for OAuth 2.0 Client Authentication and
///         Authorization Grants §3: JWT Format and Processing Requirements
///     </seealso>
///     . Enforces the claim structure (iss/sub/aud/exp/nbf/iat/jti) and the algorithm
///     allow-list. Replay protection is two-phase: <see cref="ValidateAsync" /> checks the
///     jti presence and <see cref="BurnJtiAsync" /> commits it to the replay slots once the
///     caller has verified the signature, so a forged assertion cannot poison the slots.
///     Signature verification is intentionally left to the caller, which owns the key source
///     (client secret for client_secret_jwt, registered JWKS for private_key_jwt, trust
///     table for the jwt-bearer grant).
/// </summary>
public sealed class ClientAssertionValidator([FromKeyedServices(SecurityConstants.TokenTypes.Jti)] ITokenStore<SchemataToken> slots,
    IOptions<SchemataAuthorizationOptions> options, TimeProvider? time = null)
{
    private TimeSpan ClockSkew => options.Value.TokenValidationClockSkew;

    /// <summary>Shortest time a used jti stays in the replay slots.</summary>
    internal static readonly TimeSpan ReplayFloor = TimeSpan.FromMinutes(5);

    private static readonly JsonWebTokenHandler Handler = new();

    private readonly TimeProvider _time = time ?? TimeProvider.System;

    /// <summary>
    ///     Validates the <c>client_assertion</c> JWT and returns the parsed token for
    ///     signature verification by the caller. The replay slots are untouched; commit
    ///     the jti with <see cref="BurnJtiAsync" /> after the signature verifies.
    /// </summary>
    /// <exception cref="OAuthException">With <c>invalid_client</c> for any rejected assertion.</exception>
    public Task<JsonWebToken> ValidateAsync(
        string                assertion,
        string                clientId,
        string                expectedIssuer,
        IReadOnlyList<string> audiences,
        ISet<string>          allowedAlgorithms,
        CancellationToken     ct = default
    ) {
        JsonWebToken token;
        try {
            token = Handler.ReadJsonWebToken(assertion);
        } catch (Exception ex) when (ex is ArgumentException or SecurityTokenMalformedException) {
            throw new OAuthException(OAuthErrors.InvalidClient, SchemataResources.INVALID_CLIENT_ASSERTION);
        }

        // RFC 8725 §3.1: keys are bound to algorithms; an unsecured JWT never authenticates a client.
        var algorithm = token.Alg;
        if (algorithm is not { Length: > 0 } || algorithm == "none" || !allowedAlgorithms.Contains(algorithm)) {
            throw new OAuthException(OAuthErrors.InvalidClient, SchemataResources.ASSERTION_ALGORITHM_REJECTED);
        }

        if (!string.Equals(token.Issuer, expectedIssuer, StringComparison.Ordinal)) {
            throw new OAuthException(OAuthErrors.InvalidClient, SchemataResources.ASSERTION_ISSUER_MISMATCH);
        }

        if (!string.Equals(token.Subject, clientId, StringComparison.Ordinal)) {
            throw new OAuthException(OAuthErrors.InvalidClient, SchemataResources.ASSERTION_SUBJECT_MISMATCH);
        }

        if (!new HashSet<string>(audiences, StringComparer.Ordinal).Overlaps(token.Audiences)) {
            throw new OAuthException(OAuthErrors.InvalidClient, SchemataResources.ASSERTION_AUDIENCE_MISMATCH);
        }

        var now = _time.GetUtcNow();
        if (!token.TryGetPayloadValue<long>("exp", out var exp)) {
            throw new OAuthException(OAuthErrors.InvalidClient, SchemataResources.ASSERTION_EXPIRED);
        }

        if (!NumericDates.TryFromUnixTimeSeconds(exp, out var expires)) {
            throw new OAuthException(OAuthErrors.InvalidClient, SchemataResources.INVALID_CLIENT_ASSERTION);
        }

        if (expires <= now - ClockSkew) {
            throw new OAuthException(OAuthErrors.InvalidClient, SchemataResources.ASSERTION_EXPIRED);
        }

        if (token.TryGetPayloadValue<long>("nbf", out var nbf)) {
            if (!NumericDates.TryFromUnixTimeSeconds(nbf, out var notBefore)) {
                throw new OAuthException(OAuthErrors.InvalidClient, SchemataResources.INVALID_CLIENT_ASSERTION);
            }

            if (notBefore > now + ClockSkew) {
                throw new OAuthException(OAuthErrors.InvalidClient, SchemataResources.ASSERTION_NOT_YET_VALID);
            }
        }

        if (token.TryGetPayloadValue<long>("iat", out var iat)) {
            if (!NumericDates.TryFromUnixTimeSeconds(iat, out var issuedAt)) {
                throw new OAuthException(OAuthErrors.InvalidClient, SchemataResources.INVALID_CLIENT_ASSERTION);
            }

            if (issuedAt > now + ClockSkew) {
                throw new OAuthException(OAuthErrors.InvalidClient, SchemataResources.ASSERTION_NOT_YET_VALID);
            }
        }

        if (string.IsNullOrWhiteSpace(token.Id)) {
            throw new OAuthException(OAuthErrors.InvalidClient, SchemataResources.ASSERTION_JTI_REQUIRED);
        }

        return Task.FromResult(token);
    }

    /// <summary>
    ///     Commits the <c>jti</c> of a token returned by <see cref="ValidateAsync" /> to the
    ///     replay slots. Callers MUST invoke this only after the assertion signature has been
    ///     verified, so that a forged assertion cannot poison the slots and deny the
    ///     legitimate holder of the same <c>jti</c>. A <c>jti</c> already present is a replay.
    /// </summary>
    /// <exception cref="OAuthException">With <c>invalid_client</c> when the jti was already burned.</exception>
    public async Task BurnJtiAsync(JsonWebToken token, CancellationToken ct = default) {
        var lifetime = ReplayFloor;
        if (token.TryGetPayloadValue<long>("exp", out var exp)
         && NumericDates.TryFromUnixTimeSeconds(exp, out var expires)) {
            var remaining = expires + ClockSkew - _time.GetUtcNow();
            if (remaining > lifetime) {
                lifetime = remaining;
            }
        }

        var marker = Guid.NewGuid().ToString("n");
        var row    = await slots.GetOrCreateAsync(null, "assertion", $"jti:{token.Id}", marker, lifetime, ct);

        if (row.Value != marker) {
            throw new OAuthException(OAuthErrors.InvalidClient, SchemataResources.ASSERTION_REPLAYED);
        }
    }
}
