using System.Linq;
using System.Security.Claims;
using Schemata.Abstractions;
using Schemata.Authorization.Skeleton;

namespace Schemata.Authorization.Foundation.Extensions;

/// <summary>
///     Extension methods for checking OAuth 2.0 scope grants on a <see cref="ClaimsPrincipal" />, per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-3.3">
///         RFC 6749: The OAuth 2.0 Authorization
///         Framework §3.3: Access Token Scope
///     </seealso>
///     .
/// </summary>
/// <remarks>
///     Checks both the single scope claim value and multiple individual scope claims, ensuring compatibility
///     with different token serialization formats.
/// </remarks>
internal static class ClaimsPrincipalExtensions
{
    private const string ScopeClaim = SchemataConstants.Claims.Scope;

    /// <summary>Determines whether <paramref name="principal" /> has been granted the specified <paramref name="scope" />.</summary>
    /// <param name="principal">The claims principal representing the authenticated user.</param>
    /// <param name="scope">The scope name to check (e.g. <c>"openid"</c>, <c>"profile"</c>).</param>
    /// <returns><c>true</c> if the scope was granted; otherwise <c>false</c>.</returns>
    internal static bool HasScope(this ClaimsPrincipal principal, string scope) {
        var value = principal.FindFirstValue(ScopeClaim);
        if (value is not null) {
            return ScopeParser.Contains(value, scope);
        }

        return principal.FindAll(ScopeClaim).Any(c => c.Value == scope);
    }
}
