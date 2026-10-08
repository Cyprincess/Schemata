using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Identity.Foundation.Commands;
using Schemata.Identity.Skeleton;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Models;

namespace Schemata.Identity.Foundation.Controllers;

public sealed partial class AuthenticateController<TUser>
    where TUser : SchemataUser, new()
{
    /// <summary>Registers a user and signs in the created account.</summary>
    [HttpPost(nameof(Register))]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken ct) {
        var result = await dispatcher.SendAsync<RegisterUserRequest<TUser>, IdentityResult<ClaimsPrincipal>>(
            new(request, HttpContext.User), ct);
        return result.Status switch {
            IdentityStatus.Success   => await BearerSignInAsync(result.Data!, request.UseCookies == true),
            IdentityStatus.Challenge => Challenge(),
            var _                    => throw new NoContentException(),
        };
    }

    /// <summary>Authenticates a user and issues sign-in credentials.</summary>
    [HttpPost(nameof(Login))]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct) {
        var result = await dispatcher.SendAsync<LoginUserRequest<TUser>, IdentityResult<Unit>>(
            new(request, HttpContext.User), ct);
        return result.Status switch {
            IdentityStatus.Success   => new EmptyResult(),
            IdentityStatus.Challenge => Challenge(),
            var _                    => throw new NoContentException(),
        };
    }

    /// <summary>Refreshes bearer credentials from a refresh token.</summary>
    [HttpPost(nameof(Refresh))]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request, CancellationToken ct) {
        var protector = bearer.Get(IdentityConstants.BearerScheme).RefreshTokenProtector;
        if (protector is null) {
            throw new NotFoundException();
        }

        var ticket = protector.Unprotect(request.RefreshToken);
        var result = await dispatcher.SendAsync<RefreshUserRequest<TUser>, IdentityResult<ClaimsPrincipal>>(
            new(ticket, HttpContext.User), ct);
        return result.Status switch {
            IdentityStatus.Success   => await BearerSignInAsync(result.Data!, renewal: true),
            IdentityStatus.Challenge => Challenge(),
            var _                    => throw new NoContentException(),
        };
    }

    /// <summary>
    ///     Signs out the authenticated user. Observers run before the controller performs its
    ///     final scheme sign-outs and may clear tickets they own while ending session authority.
    ///     At most one observer may provide the rendered response. Conflicts and observer failures
    ///     stop the controller's remaining final sign-outs; completed observer effects remain.
    /// </summary>
    [Authorize]
    [HttpPost(nameof(SignOut))]
    public async Task<IActionResult> SignOut(CancellationToken ct) {
        HostSignOutResponse? response = null;
        foreach (var observer in observers) {
            var observed = await observer.OnSigningOutAsync(HttpContext.User, ct);
            if (observed is null) {
                continue;
            }

            if (response is not null) {
                throw new InvalidOperationException(
                    "Conflicting sign-out responses: at most one sign-out observer may supply a response.");
            }

            response = observed;
        }

        await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
        await HttpContext.SignOutAsync(IdentityConstants.BearerScheme);
        if (response is not null) {
            return Content(response.Body, response.ContentType);
        }

        throw new NoContentException();
    }

    private async Task<IActionResult> BearerSignInAsync(ClaimsPrincipal principal, bool useCookies = false, bool renewal = false) {
        // Host authentication completes here for fresh sign-ins: observers may bind session
        // identity to the ticket before either transport serializes it. A renewal replays an
        // already-issued ticket whose session lineage is preserved by the refresh rebuild, so
        // observers stay off it — no fresh minting or mirror rebinding on browserless renewal.
        if (!renewal) {
            foreach (var observer in observers) {
                await observer.OnSigningInAsync(principal, HttpContext.User, HttpContext.RequestAborted);
            }
        }

        if (useCookies) {
            await HttpContext.SignInAsync(IdentityConstants.ApplicationScheme, principal);
        }

        return SignIn(principal, IdentityConstants.BearerScheme);
    }
}
