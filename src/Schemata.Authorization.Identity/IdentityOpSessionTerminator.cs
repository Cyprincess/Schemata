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
///     Signs the user out of ASP.NET Core Identity when an OP session is invalidated.
/// </summary>
/// <remarks>
///     Sign-out names the host schemes explicitly: the ASP.NET Core host has no
///     <c>DefaultSignOutScheme</c> when only the Schemata bearer/code schemes are registered,
///     and a scheme-less <see cref="AuthenticationHttpContextExtensions.SignOutAsync(HttpContext)" />
///     would throw and trip the end-session fail-closed path.
/// </remarks>
public sealed class IdentityOpSessionTerminator(
    IHttpContextAccessor                      accessor,
    IOptions<SchemataAuthorizationOptions>    options
) : IOpSessionTerminator
{
    public async Task TerminateAsync(
        ClaimsPrincipal? principal,
        string?          subject,
        string?          sessionId,
        CancellationToken ct = default
    ) {
        if (accessor.HttpContext is not { } http) {
            return;
        }

        await http.SignOutAsync(IdentityConstants.ApplicationScheme);
        await http.SignOutAsync(options.Value.CodeScheme);
    }

}