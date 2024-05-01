using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Schemata.Identity.Skeleton.Models;

namespace Schemata.Identity.Foundation.Controllers;

public sealed partial class AuthenticateController : ControllerBase
{
    [Authorize]
    [HttpGet("~/Account/Profile")]
    public async Task<IActionResult> Profile() {
        if (await userManager.GetUserAsync(User) is not { } user) {
            return NotFound();
        }

        var store = await userManager.ToClaimsAsync(user);

        return Ok(store);
    }

    [Authorize]
    [HttpPut("~/Account/Profile/Email")]
    public async Task<IActionResult> Email([FromBody] ProfileRequest request) {
        if (!options.CurrentValue.AllowEmailChange) {
            return NotFound();
        }

        if (await userManager.GetUserAsync(User) is not { } user) {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(request.EmailAddress)) {
            return BadRequest();
        }

        if (string.Equals(request.EmailAddress, user.Email, StringComparison.InvariantCultureIgnoreCase)) {
            return Accepted();
        }

        await SendConfirmationCodeAsync(user, request.EmailAddress, null);

        return Accepted();
    }

    [Authorize]
    [HttpPut("~/Account/Profile/Phone")]
    public async Task<IActionResult> Phone([FromBody] ProfileRequest request) {
        if (!options.CurrentValue.AllowPhoneNumberChange) {
            return NotFound();
        }

        if (await userManager.GetUserAsync(User) is not { } user) {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(request.PhoneNumber)) {
            return BadRequest();
        }

        if (string.Equals(request.PhoneNumber, user.PhoneNumber, StringComparison.InvariantCultureIgnoreCase)) {
            return Accepted();
        }

        await SendConfirmationCodeAsync(user, null, request.PhoneNumber);

        return Accepted();
    }

    [Authorize]
    [HttpPut("~/Account/Profile/Password")]
    public async Task<IActionResult> Password([FromBody] ProfileRequest request) {
        if (!options.CurrentValue.AllowPasswordChange) {
            return NotFound();
        }

        if (await userManager.GetUserAsync(User) is not { } user) {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(request.OldPassword) || string.IsNullOrWhiteSpace(request.NewPassword)) {
            return BadRequest();
        }

        if (string.Equals(request.NewPassword, request.OldPassword, StringComparison.InvariantCultureIgnoreCase)) {
            return NoContent();
        }

        await userManager.ChangePasswordAsync(user, request.OldPassword, request.NewPassword);

        return NoContent();
    }

    [Authorize]
    [HttpGet(nameof(Authenticator))]
    public async Task<IActionResult> Authenticator() {
        if (!options.CurrentValue.AllowTwoFactorAuthentication) {
            return NotFound();
        }

        if (await userManager.GetUserAsync(User) is not { } user) {
            return NotFound();
        }

        var result = new AuthenticatorResponse {
            IsTwoFactorEnabled = await userManager.GetTwoFactorEnabledAsync(user),
        };

        if (!result.IsTwoFactorEnabled) {
            await userManager.ResetAuthenticatorKeyAsync(user);
            var key = await userManager.GetAuthenticatorKeyAsync(user);
            if (string.IsNullOrEmpty(key)) {
                throw new NotSupportedException("The user manager must produce an authenticator key after reset.");
            }

            var codes = await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);

            result.SharedKey     = key;
            result.RecoveryCodes = codes?.ToArray();
        } else {
            result.IsMachineRemembered = await signInManager.IsTwoFactorClientRememberedAsync(user);
            result.RecoveryCodesLeft   = await userManager.CountRecoveryCodesAsync(user);
        }

        return Ok(result);
    }

    [Authorize]
    [HttpPost(nameof(Authenticator))]
    public async Task<IActionResult> Enroll([FromBody] AuthenticatorRequest request) {
        if (!options.CurrentValue.AllowTwoFactorAuthentication) {
            return NotFound();
        }

        if (await userManager.GetUserAsync(User) is not { } user) {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(request.TwoFactorCode)) {
            return BadRequest();
        }

        if (!await userManager.VerifyTwoFactorTokenAsync(user, userManager.Options.Tokens.AuthenticatorTokenProvider, request.TwoFactorCode)) {
            return BadRequest();
        }

        await userManager.SetTwoFactorEnabledAsync(user, true);

        return NoContent();
    }

    [Authorize]
    [HttpPatch(nameof(Authenticator))]
    public async Task<IActionResult> Downgrade([FromBody] AuthenticatorRequest request) {
        if (!options.CurrentValue.AllowTwoFactorAuthentication) {
            return NotFound();
        }

        if (await userManager.GetUserAsync(User) is not { } user) {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(request.TwoFactorCode)
         || string.IsNullOrWhiteSpace(request.TwoFactorRecoveryCode)) {
            return BadRequest();
        }

        var passed = request switch {
            var _ when !string.IsNullOrWhiteSpace(request.TwoFactorCode) => await userManager.VerifyTwoFactorTokenAsync(user, userManager.Options.Tokens.AuthenticatorTokenProvider, request.TwoFactorCode),
            var _ when !string.IsNullOrWhiteSpace(request.TwoFactorRecoveryCode) => (await userManager.RedeemTwoFactorRecoveryCodeAsync(user, request.TwoFactorRecoveryCode)).Succeeded,
            var _ => false,
        };

        if (!passed) {
            return BadRequest();
        }

        await userManager.SetTwoFactorEnabledAsync(user, false);
        await userManager.ResetAuthenticatorKeyAsync(user);

        return NoContent();
    }
}
