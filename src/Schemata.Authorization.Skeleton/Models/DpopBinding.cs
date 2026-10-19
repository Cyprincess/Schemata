namespace Schemata.Authorization.Skeleton.Models;

/// <summary>
///     AdviceContext record carrying the RFC 7638 thumbprint of the DPoP public key a
///     token request was authenticated with, per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc9449.html#section-5">
///         RFC 9449: OAuth 2.0 Demonstrating Proof-of-Possession at the Application Layer
///         (DPoP) §5: DPoP Access Token Request
///     </seealso>
///     . When present, the access token claims carry a
///     <seealso href="https://www.rfc-editor.org/rfc/rfc9449.html#section-6.1">
///         §6.1: JWK Thumbprint Confirmation Method
///     </seealso>
///     <c>cnf.jkt</c> member and the response <c>token_type</c> is <c>DPoP</c>.
/// </summary>
public sealed record DpopBinding(string Jkt);
