using System.Linq;
using System.Security.Claims;
using System.Text.Json;
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
    private const string ScopeClaim = AuthorizationConstants.Claims.Scope;

    /// <summary>Determines whether <paramref name="principal" /> has been granted the specified <paramref name="scope" />.</summary>
    /// <param name="principal">The claims principal representing the authenticated user.</param>
    /// <param name="scope">The scope name to check (e.g. <c>"openid"</c>, <c>"profile"</c>).</param>
    /// <returns><c>true</c> if the scope was granted; otherwise <c>false</c>.</returns>
    internal static bool HasScope(this ClaimsPrincipal principal, string scope) {
        return principal.FindAll(ScopeClaim).Any(claim => ScopeParser.Contains(claim.Value, scope));
    }

    /// <summary>
    ///     Returns the JWK thumbprint bound to the principal's access token through the <c>cnf</c>
    ///     member
    ///     (<seealso href="https://www.rfc-editor.org/rfc/rfc9449.html#section-6.1">
    ///         RFC 9449 §6.1: JWK Thumbprint Confirmation
    ///     </seealso>
    ///     ); returns <see langword="null" /> when the claim is absent or carries no jkt object.
    /// </summary>
    internal static string? ReadDpopThumbprint(this ClaimsPrincipal principal) {
        var json = principal.FindFirstValue(AuthorizationConstants.Claims.Cnf);
        if (string.IsNullOrWhiteSpace(json)) {
            return null;
        }

        try {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty(AuthorizationConstants.Claims.Jkt, out var jkt)
                && jkt.ValueKind == JsonValueKind.String
                    ? jkt.GetString()
                    : null;
        } catch (JsonException) {
            // A cnf claim that is not a JSON object carries no jkt binding.
            return null;
        }
    }
}
