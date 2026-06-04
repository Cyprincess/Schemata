using System;
using System.Linq;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using Schemata.Security.Skeleton;

namespace Schemata.Security.Foundation;

/// <summary>
///     Matches AIP-style dot-separated permissions against claims, with single-segment wildcard (*) support.
///     The * matches any single segment at the same position; namespace position (first of 3+) is excluded.
/// </summary>
public sealed class DefaultPermissionMatcher(IOptions<SchemataSecurityOptions> options) : IPermissionMatcher
{
    #region IPermissionMatcher Members

    public bool IsMatch(ClaimsPrincipal principal, string permission) {
        var type   = options.Value.PermissionClaimType;
        var claims = principal.FindAll(type).Select(c => c.Value).ToList();

        if (claims.Count == 0) return false;
        if (claims.Contains(permission)) return true;

        var pSegs = permission.Split('.');

        foreach (var claim in claims) {
            var cSegs = claim.Split('.');
            if (cSegs.Length != pSegs.Length) continue;

            var starIdx = Array.IndexOf(cSegs, "*");

            if (starIdx < 0) {
                continue; // no wildcard — already checked exact match
            }

            if (starIdx == 0 && cSegs.Length > 2) {
                continue; // namespace cannot be wildcard (3+ segments)
            }

            if (Array.LastIndexOf(cSegs, "*") != starIdx) {
                continue; // only one wildcard supported
            }

            var match = true;
            for (var i = 0; i < cSegs.Length; i++) {
                if (cSegs[i] == "*") {
                    continue;
                }

                if (cSegs[i] == pSegs[i]) {
                    continue;
                }

                match = false;
                break;
            }

            if (match) return true;
        }

        return false;
    }

    #endregion
}
