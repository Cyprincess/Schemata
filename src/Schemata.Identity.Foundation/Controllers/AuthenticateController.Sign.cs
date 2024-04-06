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

        var result = await _users.CreateAsync(user, request.Password);

        if (!result.Succeeded) {
            return BadRequest(result.Errors);
        }

        return Ok();
    }

    [HttpPost(nameof(Login))]
    public async Task<IActionResult> Login([FromBody] LoginRequest request) {
        var result = await _sign.PasswordSignInAsync(request.Username, request.Password, false, true);

        if (result.RequiresTwoFactor)
        {
            if (!string.IsNullOrEmpty(request.TwoFactorCode))
            {
                result = await _sign.TwoFactorAuthenticatorSignInAsync(request.TwoFactorCode, false, false);
            }
            else if (!string.IsNullOrEmpty(request.TwoFactorRecoveryCode))
            {
                result = await _sign.TwoFactorRecoveryCodeSignInAsync(request.TwoFactorRecoveryCode);
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
        var protector = _options.Get(IdentityConstants.ApplicationScheme).RefreshTokenProtector;
        var ticket         = protector.Unprotect(request.RefreshToken);

        if (ticket?.Properties?.ExpiresUtc is not { } expiresUtc ||
            DateTimeOffset.UtcNow >= expiresUtc ||
            await _sign.ValidateSecurityStampAsync(ticket.Principal) is not { } user)
        {
            return Challenge();
        }

        var principal = await _sign.CreateUserPrincipalAsync(user);
        return SignIn(principal, IdentityConstants.ApplicationScheme);
    }
}
