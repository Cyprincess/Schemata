using System;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Foundation;

/// <summary>
///     Canonical issuer helpers: startup validation of the configured issuer identifier,
///     derivation of the well-known metadata/JWKS route paths from it, and composition of
///     issuer endpoint URLs.
/// </summary>
internal static class CanonicalIssuer
{
    /// <summary>
    ///     Validates the configured issuer identifier against the OpenID Connect Discovery
    ///     issuer requirements: an absolute HTTPS URI with an authority and no userinfo, query,
    ///     or fragment. Schemata additionally rejects a non-root trailing slash instead of
    ///     normalizing it; the root path <c>/</c> remains valid.
    /// </summary>
    /// <param name="issuer">The configured issuer identifier.</param>
    /// <returns>The issuer parsed as a <see cref="Uri" />.</returns>
    public static Uri Validate(string? issuer) {
        if (!Uri.TryCreate(issuer, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || string.IsNullOrEmpty(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || issuer.Contains('?')
            || issuer.Contains('#')
            || uri.AbsolutePath is { Length: > 1 } path && path.EndsWith('/')) {
            throw new InvalidOperationException(
                $"The issuer '{issuer}' must be an absolute HTTPS URI with an authority and without userinfo, query, fragment, or a non-root trailing slash.");
        }

        return uri;
    }

    /// <summary>The issuer's path component with any root slash removed (<c>""</c> for a root issuer).</summary>
    public static string Path(Uri issuer) => issuer.AbsolutePath.TrimEnd('/');

    /// <summary>Composes an issuer endpoint URL without a double slash at the boundary.</summary>
    public static string Combine(string? issuer, string endpoint) => issuer?.TrimEnd('/') + endpoint;

    /// <summary>
    ///     OIDC discovery route path: <c>{issuerPath}/.well-known/openid-configuration</c>, per
    ///     <seealso href="https://openid.net/specs/openid-connect-discovery-1_0.html#ProviderConfig">
    ///         OpenID Connect Discovery 1.0 §§4-4.1: Obtaining OpenID Provider Configuration Information
    ///     </seealso>
    ///     .
    /// </summary>
    public static string OpenIdConfiguration(Uri issuer) => Path(issuer) + Endpoints.Discovery;

    /// <summary>
    ///     OAuth 2.0 authorization server metadata route path:
    ///     <c>/.well-known/oauth-authorization-server{issuerPath}</c>, per
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc8414.html#section-3.1">
    ///         RFC 8414: OAuth 2.0 Authorization Server Metadata §3.1: Authorization Server Metadata Request
    ///     </seealso>
    ///     .
    /// </summary>
    public static string OAuthAuthorizationServer(Uri issuer) => Endpoints.OAuthAuthorizationServer + Path(issuer);

    /// <summary>JWKS route path: <c>{issuerPath}/.well-known/jwks</c>.</summary>
    public static string Jwks(Uri issuer) => Path(issuer) + Endpoints.Jwks;
}
