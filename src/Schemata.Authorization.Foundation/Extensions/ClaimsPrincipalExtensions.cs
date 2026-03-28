using System.Linq;
using System.Security.Claims;
using Schemata.Abstractions;
using Schemata.Authorization.Skeleton;

namespace Schemata.Authorization.Foundation.Extensions;

internal static class ClaimsPrincipalExtensions
{
    private const string ScopeClaim = SchemataConstants.Claims.Scope;

    internal static bool HasScope(this ClaimsPrincipal principal, string scope) {
        var value = principal.FindFirstValue(ScopeClaim);
        if (value is not null) {
            return ScopeParser.Contains(value, scope);
        }

        return principal.FindAll(ScopeClaim).Any(c => c.Value == scope);
    }
}
