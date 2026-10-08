using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Schemata.Authorization.Skeleton.Services;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation.Services;

/// <summary>Projects authentication evidence without inventing absent values.</summary>
internal static class AuthenticationContextExtensions
{
    internal static async System.Threading.Tasks.Task<AuthenticationContext?> ResolveAsync(
        ClaimsPrincipal? principal,
        AuthenticationContext? existing,
        string? sessionId,
        string sessionClaimType,
        IAuthenticationContextProvider? provider,
        System.Threading.CancellationToken ct
    ) {
        var evidence = PrincipalWithSession(principal, sessionClaimType, sessionId);
        var fresh = provider is null
            ? Read(evidence.Claims)
            : await provider.GetContextAsync(evidence, ct);

        // Fresh evidence describes the approving principal's own authentication and replaces any
        // inherited event; without fresh evidence the caller-vetted continuation is inherited.
        return HasEvidence(fresh) ? fresh : existing;
    }

    internal static void Apply(List<Claim> claims, AuthenticationContext? context, bool destinations) {
        Remove(claims, Claims.Acr);
        Remove(claims, Claims.Amr);
        Remove(claims, Claims.AuthTime);

        if (context is null) {
            return;
        }

        if (!string.IsNullOrWhiteSpace(context.Acr)) {
            claims.Add(Claim(Claims.Acr, context.Acr, ClaimValueTypes.String, destinations));
        }

        if (context.Amr is { Count: > 0 }) {
            claims.Add(Claim(Claims.Amr, JsonSerializer.Serialize(context.Amr), JsonClaimValueTypes.Json, destinations));
        }

        if (context.AuthTime is { } authTime) {
            claims.Add(Claim(
                Claims.AuthTime,
                authTime.ToString(CultureInfo.InvariantCulture),
                ClaimValueTypes.Integer64,
                destinations));
        }
    }

    internal static AuthenticationContext? Read(IEnumerable<Claim> claims) {
        var acr = claims.FirstOrDefault(claim => claim.Type == Claims.Acr)?.Value;
        var amr = new List<string>();
        foreach (var claim in claims.Where(claim => claim.Type == Claims.Amr)) {
            if (claim.ValueType == JsonClaimValueTypes.Json) {
                try {
                    var values = JsonSerializer.Deserialize<string[]>(claim.Value) ?? [];
                    AddDistinct(amr, values);
                    continue;
                } catch (JsonException) {
                    // Fall through to the scalar representation supplied by some host schemes.
                }
            }

            AddDistinct(amr, claim.Value.Split(' ', System.StringSplitOptions.RemoveEmptyEntries));
        }

        var authTime = claims.FirstOrDefault(claim => claim.Type == Claims.AuthTime)?.Value;
        long? parsed = long.TryParse(authTime, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

        var context = new AuthenticationContext(
            string.IsNullOrWhiteSpace(acr) ? null : acr,
            amr,
            parsed);
        return HasEvidence(context) ? context : null;
    }

    internal static bool HasEvidence(AuthenticationContext? context) {
        return context is not null
            && (!string.IsNullOrWhiteSpace(context.Acr)
                || context.Amr is { Count: > 0 }
                || context.AuthTime is not null);
    }

    internal static void Stamp(this List<Claim> claims, AuthenticationContext context) {
        Apply(claims, context, destinations: false);
    }

    private static ClaimsPrincipal PrincipalWithSession(
        ClaimsPrincipal? principal,
        string claimType,
        string? sessionId
    ) {
        var identity = principal?.Identity is ClaimsIdentity found
            ? new ClaimsIdentity(found)
            : new ClaimsIdentity();
        if (!string.IsNullOrWhiteSpace(sessionId)) {
            var current = identity.FindFirst(claimType);
            if (current is not null && !string.Equals(current.Value, sessionId, System.StringComparison.Ordinal)) {
                identity.RemoveClaim(current);
            }
            if (!identity.HasClaim(claim => claim.Type == claimType)) {
                identity.AddClaim(new(claimType, sessionId));
            }
        }

        return new(identity);
    }

    private static Claim Claim(string type, string value, string valueType, bool destinations) {
        var claim = new Claim(type, value, valueType);
        if (destinations) {
            claim.Properties[ClaimDestinations.IdentityToken] = Parameters.Token;
            claim.Properties[ClaimDestinations.AccessToken]   = Parameters.Token;
        }

        return claim;
    }

    private static void Remove(List<Claim> claims, string type) {
        for (var i = claims.Count - 1; i >= 0; i--) {
            if (claims[i].Type == type) {
                claims.RemoveAt(i);
            }
        }
    }

    private static void AddDistinct(List<string> target, IEnumerable<string> values) {
        foreach (var value in values) {
            if (!string.IsNullOrWhiteSpace(value) && !target.Contains(value, System.StringComparer.Ordinal)) {
                target.Add(value);
            }
        }
    }
}
