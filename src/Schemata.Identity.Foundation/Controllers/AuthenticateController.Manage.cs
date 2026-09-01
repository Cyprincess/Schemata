using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Schemata.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Identity.Foundation.Commands;
using Schemata.Identity.Foundation.Queries;
using Schemata.Identity.Skeleton;
using Schemata.Identity.Skeleton.Claims;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Models;

namespace Schemata.Identity.Foundation.Controllers;

public sealed partial class AuthenticateController<TUser>
    where TUser : SchemataUser, new()
{
    /// <summary>Returns the authenticated user's profile claims.</summary>
    [Authorize]
    [HttpGet("~/Account/Profile")]
    public async Task<IActionResult> Profile(CancellationToken ct) {
        var result = await dispatcher.SendAsync<GetUserProfileQuery<TUser>, IdentityResult<ClaimsStore>>(
            new(HttpContext.User), ct);
        return result.Status switch {
            IdentityStatus.Success   => Ok(result.Data),
            IdentityStatus.Challenge => Challenge(),
            var _                    => throw new NoContentException(),
        };
    }

    /// <summary>Starts an email-address change for the authenticated user.</summary>
    [Authorize]
    [HttpPut("~/Account/Profile/Email")]
    public async Task<IActionResult> Email([FromBody] ProfileRequest request, CancellationToken ct) {
        var result = await dispatcher.SendAsync<ChangeUserEmailRequest<TUser>, IdentityResult<Unit>>(
            new(request, HttpContext.User), ct);
        return result.Status switch {
            IdentityStatus.Success   => Accepted(),
            IdentityStatus.Challenge => Challenge(),
            var _                    => throw new NoContentException(),
        };
    }

    /// <summary>Starts a phone-number change for the authenticated user.</summary>
    [Authorize]
    [HttpPut("~/Account/Profile/Phone")]
    public async Task<IActionResult> Phone([FromBody] ProfileRequest request, CancellationToken ct) {
        var result = await dispatcher.SendAsync<ChangeUserPhoneRequest<TUser>, IdentityResult<Unit>>(
            new(request, HttpContext.User), ct);
        return result.Status switch {
            IdentityStatus.Success   => Accepted(),
            IdentityStatus.Challenge => Challenge(),
            var _                    => throw new NoContentException(),
        };
    }

    /// <summary>Changes the authenticated user's password.</summary>
    [Authorize]
    [HttpPut("~/Account/Profile/Password")]
    public async Task<IActionResult> Password([FromBody] ProfileRequest request, CancellationToken ct) {
        var result = await dispatcher.SendAsync<ChangeUserPasswordRequest<TUser>, IdentityResult<Unit>>(
            new(request, HttpContext.User), ct);
        return result.Status switch {
            IdentityStatus.Success   => NoContent(),
            IdentityStatus.Challenge => Challenge(),
            var _                    => throw new NoContentException(),
        };
    }

    /// <summary>Returns two-factor authenticator enrollment state for the authenticated user.</summary>
    [Authorize]
    [HttpGet(nameof(Authenticator))]
    public async Task<IActionResult> Authenticator(CancellationToken ct) {
        var result = await dispatcher.SendAsync<
            GetUserAuthenticatorRequest<TUser>,
            IdentityResult<AuthenticatorResponse>>(new(HttpContext.User), ct);
        return result.Status switch {
            IdentityStatus.Success   => Ok(result.Data),
            IdentityStatus.Challenge => Challenge(),
            var _                    => throw new NoContentException(),
        };
    }

    /// <summary>Enables two-factor authenticator sign-in for the authenticated user.</summary>
    [Authorize]
    [HttpPost(nameof(Authenticator))]
    public async Task<IActionResult> Enroll([FromBody] AuthenticatorRequest request, CancellationToken ct) {
        var result = await dispatcher.SendAsync<EnrollUserAuthenticatorRequest<TUser>, IdentityResult<Unit>>(
            new(request, HttpContext.User), ct);
        return result.Status switch {
            IdentityStatus.Success   => NoContent(),
            IdentityStatus.Challenge => Challenge(),
            var _                    => throw new NoContentException(),
        };
    }

    /// <summary>Disables two-factor authenticator sign-in for the authenticated user.</summary>
    [Authorize]
    [HttpPatch(nameof(Authenticator))]
    public async Task<IActionResult> Downgrade([FromBody] AuthenticatorRequest request, CancellationToken ct) {
        var result = await dispatcher.SendAsync<DowngradeUserAuthenticatorRequest<TUser>, IdentityResult<Unit>>(
            new(request, HttpContext.User), ct);
        return result.Status switch {
            IdentityStatus.Success   => NoContent(),
            IdentityStatus.Challenge => Challenge(),
            var _                    => throw new NoContentException(),
        };
    }
}
