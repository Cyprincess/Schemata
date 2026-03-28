using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Schemata.Abstractions.Exceptions;
using Schemata.Identity.Skeleton;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Models;

namespace Schemata.Identity.Foundation.Controllers;

public sealed partial class AuthenticateController<TUser>
    where TUser : SchemataUser, new()
{
    [Authorize]
    [HttpGet("~/Account/Profile")]
    public async Task<IActionResult> Profile(CancellationToken ct) {
        var result = await handler.ProfileAsync(HttpContext.User, ct);
        return result.Status switch {
            IdentityStatus.Success   => Ok(result.Data),
            IdentityStatus.Challenge => Challenge(),
            var _                    => throw new NoContentException(),
        };
    }

    [Authorize]
    [HttpPut("~/Account/Profile/Email")]
    public async Task<IActionResult> Email([FromBody] ProfileRequest request, CancellationToken ct) {
        var result = await handler.ChangeEmailAsync(request, HttpContext.User, ct);
        return result.Status switch {
            IdentityStatus.Success   => Accepted(),
            IdentityStatus.Challenge => Challenge(),
            var _                    => throw new NoContentException(),
        };
    }

    [Authorize]
    [HttpPut("~/Account/Profile/Phone")]
    public async Task<IActionResult> Phone([FromBody] ProfileRequest request, CancellationToken ct) {
        var result = await handler.ChangePhoneAsync(request, HttpContext.User, ct);
        return result.Status switch {
            IdentityStatus.Success   => Accepted(),
            IdentityStatus.Challenge => Challenge(),
            var _                    => throw new NoContentException(),
        };
    }

    [Authorize]
    [HttpPut("~/Account/Profile/Password")]
    public async Task<IActionResult> Password([FromBody] ProfileRequest request, CancellationToken ct) {
        var result = await handler.ChangePasswordAsync(request, HttpContext.User, ct);
        return result.Status switch {
            IdentityStatus.Success   => NoContent(),
            IdentityStatus.Challenge => Challenge(),
            var _                    => throw new NoContentException(),
        };
    }

    [Authorize]
    [HttpGet(nameof(Authenticator))]
    public async Task<IActionResult> Authenticator(CancellationToken ct) {
        var result = await handler.AuthenticatorAsync(HttpContext.User, ct);
        return result.Status switch {
            IdentityStatus.Success   => Ok(result.Data),
            IdentityStatus.Challenge => Challenge(),
            var _                    => throw new NoContentException(),
        };
    }

    [Authorize]
    [HttpPost(nameof(Authenticator))]
    public async Task<IActionResult> Enroll([FromBody] AuthenticatorRequest request, CancellationToken ct) {
        var result = await handler.EnrollAsync(request, HttpContext.User, ct);
        return result.Status switch {
            IdentityStatus.Success   => NoContent(),
            IdentityStatus.Challenge => Challenge(),
            var _                    => throw new NoContentException(),
        };
    }

    [Authorize]
    [HttpPatch(nameof(Authenticator))]
    public async Task<IActionResult> Downgrade([FromBody] AuthenticatorRequest request, CancellationToken ct) {
        var result = await handler.DowngradeAsync(request, HttpContext.User, ct);
        return result.Status switch {
            IdentityStatus.Success   => NoContent(),
            IdentityStatus.Challenge => Challenge(),
            var _                    => throw new NoContentException(),
        };
    }
}
