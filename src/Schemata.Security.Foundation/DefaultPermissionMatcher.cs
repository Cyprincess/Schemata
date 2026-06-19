using System;
using System.Linq;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using Schemata.Security.Skeleton;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Security.Foundation;

/// <summary>
///     Matches AIP-style dot-separated permissions against claims, with single-segment <c>*</c> wildcard support.
///     Wildcards match any single segment at the same position after the namespace segment.
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

            var starIdx = Array.IndexOf(cSegs, Wildcards.Any);

            if (starIdx < 0) {
                continue;
            }

            if (starIdx == 0 && cSegs.Length > 2) {
                continue;
            }

            if (Array.LastIndexOf(cSegs, Wildcards.Any) != starIdx) {
                continue;
            }

            var match = true;
            for (var i = 0; i < cSegs.Length; i++) {
                if (cSegs[i] == Wildcards.Any) {
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
