using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Authorization.Skeleton.Services;

/// <summary>
///     Supplies the authentication context (<c>acr</c>, <c>amr</c>, <c>auth_time</c>) asserted for a
///     principal, per
///     <seealso href="https://openid.net/specs/openid-connect-core-1_0.html#IDToken">
///         OpenID Connect Core 1.0 §2: ID Token
///     </seealso>
///     and
///     <seealso href="https://www.rfc-editor.org/rfc/rfc8176.html">
///         RFC 8176: Authentication Method Reference Values
///     </seealso>
///     .
/// </summary>
public interface IAuthenticationContextProvider
{
    /// <summary>
    ///     Resolves a new authentication event for <paramref name="principal" />. Implementations
    ///     must tolerate a <c>null</c> or claim-less principal by returning an empty context.
    ///     Code, device, refresh, and validated exchange continuations inherit the persisted event
    ///     and do not call the provider again.
    /// </summary>
    /// <param name="principal">The authenticated principal, if any.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The authentication context for the principal.</returns>
    Task<AuthenticationContext> GetContextAsync(ClaimsPrincipal? principal, CancellationToken ct = default);
}