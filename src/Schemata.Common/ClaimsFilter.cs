using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Common;

/// <summary>
///     Filters claims based on granted OIDC scopes. The <c>sub</c> claim is always included regardless of scopes.
/// </summary>
public static class ClaimsFilter
{
    /// <summary>
    ///     Returns only the claims whose types are allowed by the given scopes, plus the <c>sub</c> claim.
    /// </summary>
    /// <param name="claims">The full set of claims.</param>
    /// <param name="scopes">The granted OIDC scopes.</param>
    /// <returns>The filtered claims.</returns>
    public static IEnumerable<Claim> Filter(IEnumerable<Claim> claims, IEnumerable<string> scopes) {
        var allowed = new HashSet<string>(scopes.Where(StandardScopes.ScopeClaims.ContainsKey)
                                                .SelectMany(s => StandardScopes.ScopeClaims[s]));
        allowed.Add(Claims.Subject);

        return claims.Where(c => allowed.Contains(c.Type));
    }
}
