using System;
using System.Collections.Generic;
using System.Linq;

namespace Schemata.Authorization.Skeleton.Services;

/// <summary>
///     Authentication strength and recentness required for a resource, expressed using
///     OpenID Connect <c>acr</c> and <c>auth_time</c> evidence.
/// </summary>
/// <param name="AcrValues">Acceptable Authentication Context Class References in preference order.</param>
/// <param name="MaxAge">Maximum elapsed time since active authentication.</param>
public sealed record ResourceAuthenticationRequirement(
    IReadOnlyList<string> AcrValues,
    TimeSpan? MaxAge)
{
    /// <summary>Evaluates the actual authentication evidence against both constraints.</summary>
    public bool IsSatisfiedBy(string? acr, long? authTime, DateTimeOffset now) {
        if (AcrValues.Count > 0
         && (acr is null || !AcrValues.Contains(acr, StringComparer.Ordinal))) {
            return false;
        }

        if (MaxAge is not { } maxAge) {
            return true;
        }

        if (authTime is not { } epoch || maxAge < TimeSpan.Zero) {
            return false;
        }

        var elapsed = (double)now.ToUnixTimeSeconds() - epoch;
        return elapsed >= 0 && elapsed <= maxAge.TotalSeconds;
    }
}
