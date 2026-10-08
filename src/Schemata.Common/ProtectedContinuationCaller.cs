using System.Security.Claims;

namespace Schemata.Common;

/// <summary>Identifies the trusted default principal identity for protected continuations.</summary>
public readonly record struct ProtectedContinuationCaller(
    bool IsAuthenticated, string? AuthenticationType, string? Subject, string? NameClaimType)
{
    /// <summary>Captures the identity selected by the executing principal and its authentication domain.</summary>
    public static ProtectedContinuationCaller Bind(ClaimsPrincipal? principal) {
        var identity = principal?.Identity;
        if (identity?.IsAuthenticated != true) return default;

        if (identity is ClaimsIdentity claims) {
            var subject = claims.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrWhiteSpace(subject)) subject = claims.FindFirst("sub")?.Value;
            if (!string.IsNullOrWhiteSpace(subject)) return new(true, identity.AuthenticationType, subject, null);
        }

        return new(true, identity.AuthenticationType, identity.Name,
            identity is ClaimsIdentity named ? named.NameClaimType : ClaimTypes.Name);
    }
}
