using System.Collections.Generic;
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
    ///     Resolves the authentication context for <paramref name="principal" />. Implementations
    ///     must tolerate a <c>null</c> or claim-less principal by returning an empty context; the
    ///     OIDC claims remain absent from minted tokens when no evidence exists.
    /// </summary>
    /// <param name="principal">The authenticated principal, if any.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The authentication context for the principal.</returns>
    Task<AuthenticationContext> GetContextAsync(ClaimsPrincipal? principal, CancellationToken ct = default);
}

/// <summary>
///     Authentication context asserted for a principal: <paramref name="Acr" /> names the context
///     class the authentication satisfied, <paramref name="Amr" /> lists the RFC 8176 method
///     references used, and <paramref name="AuthTime" /> is the Unix-seconds timestamp of the
///     authentication event (OpenID Connect Core 1.0 §2).
/// </summary>
/// <param name="Acr">Satisfied Authentication Context Class Reference, or <c>null</c> when unknown.</param>
/// <param name="Amr">Authentication method references; empty when unknown.</param>
/// <param name="AuthTime">Authentication event time in Unix seconds, or <c>null</c> when unknown.</param>
public sealed record AuthenticationContext(string? Acr, IReadOnlyList<string> Amr, long? AuthTime);
