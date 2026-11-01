using System.Collections.Generic;

namespace Schemata.Identity.Skeleton;

/// <summary>
///     Authentication Context Class References the login pipeline can satisfy, per
///     <seealso href="https://openid.net/specs/openid-connect-core-1_0.html#IDToken">
///         OpenID Connect Core 1.0 §2: ID Token
///     </seealso>
///     : an <c>acr</c> value SHOULD be an absolute URI or an RFC 6711 registered name and names
///     the Authentication Context Class the authentication satisfied, not the methods used
///     (RFC 8176 §3).
/// </summary>
public static class AuthenticationContextClasses
{
    /// <summary>Single-factor password authentication.</summary>
    public const string Password = "urn:schemata:acr:classes:password";

    /// <summary>Multi-factor authentication.</summary>
    public const string Multifactor = "urn:schemata:acr:classes:multifactor";

    /// <summary>Classes the login pipeline satisfies, strongest first.</summary>
    public static IReadOnlyList<string> Supported { get; } = [Multifactor, Password];
}
