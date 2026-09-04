using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Skeleton.Services;

namespace Schemata.Authorization.Identity;

/// <summary>
///     Host-session-backed <see cref="IOpSessionService" />: invalidation signs the end user out of
///     the ASP.NET Core host session (cookie authentication schemes), per
///     <seealso href="https://openid.net/specs/openid-connect-rpinitiated-1_0.html">
///         OpenID Connect RP-Initiated Logout 1.0 §2: Logout Request
///     </seealso>
///     .
/// </summary>
/// <remarks>
///     Sign-out names the host schemes explicitly: the ASP.NET Core host has no
///     <c>DefaultSignOutScheme</c> when only the Schemata bearer/code schemes are registered,
///     and a scheme-less <see cref="AuthenticationHttpContextExtensions.SignOutAsync(HttpContext)" />
///     would throw and trip the end-session fail-closed path.
/// </remarks>
public sealed class OpSessionService(
    IHttpContextAccessor                      accessor,
    IOptions<SchemataAuthorizationOptions>    options
) : IOpSessionService
{
    #region IOpSessionService Members

    public async Task InvalidateAsync(ClaimsPrincipal? principal, string? subject, string? sessionId, CancellationToken ct = default) {
        if (accessor.HttpContext is not { } http) {
            return;
        }

        await http.SignOutAsync(IdentityConstants.ApplicationScheme);
        await http.SignOutAsync(options.Value.CodeScheme);
    }

    #endregion
}