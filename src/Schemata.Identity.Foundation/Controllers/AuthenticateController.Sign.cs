using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Schemata.Abstractions.Exceptions;
using Schemata.Identity.Skeleton;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Models;

namespace Schemata.Identity.Foundation.Controllers;

public sealed partial class AuthenticateController<TUser>
    where TUser : SchemataUser, new()
{
    [HttpPost(nameof(Register))]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken ct) {
        var result = await handler.RegisterAsync(request, HttpContext.User, ct);
        return result.Status switch {
            IdentityStatus.Success   => await BearerSignInAsync(result.Data!, request.UseCookies == true),
            IdentityStatus.Challenge => Challenge(),
            var _                    => throw new NoContentException(),
        };
    }

    [HttpPost(nameof(Login))]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct) {
        var result = await handler.LoginAsync(request, HttpContext.User, ct);
        return result.Status switch {
            IdentityStatus.Success   => await BearerSignInAsync(result.Data!, request.UseCookies == true),
            IdentityStatus.Challenge => Challenge(),
            var _                    => throw new NoContentException(),
        };
    }

    [HttpPost(nameof(Refresh))]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request, CancellationToken ct) {
        var protector = bearer.Get(IdentityConstants.BearerScheme).RefreshTokenProtector;
        if (protector is null) {
            throw new NotFoundException();
        }

        var ticket = protector.Unprotect(request.RefreshToken);
        var result = await handler.RefreshAsync(ticket, HttpContext.User, ct);
        return result.Status switch {
            IdentityStatus.Success   => await BearerSignInAsync(result.Data!),
            IdentityStatus.Challenge => Challenge(),
            var _                    => throw new NoContentException(),
        };
    }

    [Authorize]
    [HttpPost(nameof(SignOut))]
    public async Task<IActionResult> SignOut(CancellationToken ct) {
        await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
        await HttpContext.SignOutAsync(IdentityConstants.BearerScheme);
        throw new NoContentException();
    }

    private async Task<IActionResult> BearerSignInAsync(ClaimsPrincipal principal, bool useCookies = false) {
        if (useCookies) {
            await HttpContext.SignInAsync(IdentityConstants.ApplicationScheme, principal);
        }

        return SignIn(principal, IdentityConstants.BearerScheme);
    }
}
