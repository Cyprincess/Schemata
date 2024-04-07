using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Schemata.Identity.Foundation.Models;
using Schemata.Identity.Skeleton.Entities;

namespace Schemata.Identity.Foundation.Controllers;

public partial class AuthenticateController : ControllerBase
{
    [HttpPost(nameof(Register))]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request) {
        var username = request.EmailAddress ?? request.PhoneNumber;

        var user = new SchemataUser {
            UserName    = username,
            Email       = request.EmailAddress,
            PhoneNumber = request.PhoneNumber,
        };

        var result = await UserManager.CreateAsync(user, request.Password);

        if (!result.Succeeded) {
            return BadRequest(result.Errors);
        }

        if (UserManager.Options.SignIn.RequireConfirmedAccount) {
            await SendConfirmationCodeAsync(user, request.EmailAddress, request.PhoneNumber);
        }

        return NoContent();
    }

    [HttpPost(nameof(Login))]
    public async Task<IActionResult> Login([FromBody] LoginRequest request) {
        var result = await SignInManager.PasswordSignInAsync(request.Username, request.Password, false, true);

        if (result.RequiresTwoFactor)
        {
            if (!string.IsNullOrEmpty(request.TwoFactorCode))
            {
                result = await SignInManager.TwoFactorAuthenticatorSignInAsync(request.TwoFactorCode, false, false);
            }
            else if (!string.IsNullOrEmpty(request.TwoFactorRecoveryCode))
            {
                result = await SignInManager.TwoFactorRecoveryCodeSignInAsync(request.TwoFactorRecoveryCode);
            }
        }

        if (!result.Succeeded)
        {
            return BadRequest(result.ToString());
        }

        return new EmptyResult();
    }

    [HttpPost(nameof(Refresh))]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request) {
        var protector = Options.Get(IdentityConstants.ApplicationScheme).RefreshTokenProtector;
        var ticket         = protector.Unprotect(request.RefreshToken);

        if (ticket?.Properties?.ExpiresUtc is not { } expiresUtc ||
            DateTimeOffset.UtcNow >= expiresUtc ||
            await SignInManager.ValidateSecurityStampAsync(ticket.Principal) is not { } user)
        {
            return Challenge();
        }

        var principal = await SignInManager.CreateUserPrincipalAsync(user);

        return SignIn(principal, IdentityConstants.ApplicationScheme);
    }
}
