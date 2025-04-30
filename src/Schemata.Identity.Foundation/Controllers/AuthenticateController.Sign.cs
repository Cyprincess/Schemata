using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Models;

namespace Schemata.Identity.Foundation.Controllers;

public sealed partial class AuthenticateController : ControllerBase
{
    [HttpPost(nameof(Register))]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request) {
        if (!_options.CurrentValue.AllowRegistration) {
            return NotFound();
        }

        var username = request.EmailAddress ?? request.PhoneNumber;

        var user = new SchemataUser {
            UserName    = username,
            Email       = request.EmailAddress,
            PhoneNumber = request.PhoneNumber,
        };

        var result = await _userManager.CreateAsync(user, request.Password);

        if (!result.Succeeded) {
            return BadRequest(result.Errors);
        }

        if (_userManager.Options.SignIn.RequireConfirmedAccount) {
            await SendConfirmationCodeAsync(user, request.EmailAddress, request.PhoneNumber);
        }

        return NoContent();
    }

    [HttpPost(nameof(Login))]
    public async Task<IActionResult> Login([FromBody] LoginRequest request) {
        var result = await _signInManager.PasswordSignInAsync(request.Username, request.Password, false, true);

        if (result.RequiresTwoFactor) {
            if (!string.IsNullOrWhiteSpace(request.TwoFactorCode)) {
                result = await _signInManager.TwoFactorAuthenticatorSignInAsync(request.TwoFactorCode, false, false);
            } else if (!string.IsNullOrWhiteSpace(request.TwoFactorRecoveryCode)) {
                result = await _signInManager.TwoFactorRecoveryCodeSignInAsync(request.TwoFactorRecoveryCode);
            }
        }

        if (!result.Succeeded) {
            return BadRequest(result.ToString());
        }

        return EmptyResult;
    }

    [HttpPost(nameof(Refresh))]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request) {
        var protector = _bearerToken.Get(IdentityConstants.BearerScheme).RefreshTokenProtector;
        var ticket    = protector.Unprotect(request.RefreshToken);

        if (ticket?.Properties?.ExpiresUtc is not { } expiresUtc
         || DateTimeOffset.UtcNow >= expiresUtc
         || await _signInManager.ValidateSecurityStampAsync(ticket.Principal) is not { } user) {
            return Challenge();
        }

        var principal = await _signInManager.CreateUserPrincipalAsync(user);

        return SignIn(principal, IdentityConstants.BearerScheme);
    }
}
