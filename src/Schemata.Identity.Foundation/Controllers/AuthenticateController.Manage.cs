using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Schemata.Identity.Foundation.Models;

namespace Schemata.Identity.Foundation.Controllers;

public partial class AuthenticateController : ControllerBase
{
    [Authorize]
    [HttpGet("~/Account/Profile")]
    public async Task<IActionResult> Profile() {
        if (await UserManager.GetUserAsync(User) is not { } user) {
            return NotFound();
        }

        var store = await UserManager.ToClaimsAsync(user);

        return Ok(store);
    }

    [Authorize]
    [HttpPut("~/Account/Profile/Email")]
    public async Task<IActionResult> Email([FromBody] ProfileRequest request) {
        if (!Options.CurrentValue.AllowEmailChange) {
            return NotFound();
        }

        if (await UserManager.GetUserAsync(User) is not { } user) {
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
        if (!Options.CurrentValue.AllowPhoneNumberChange) {
            return NotFound();
        }

        if (await UserManager.GetUserAsync(User) is not { } user) {
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
        if (!Options.CurrentValue.AllowPasswordChange) {
            return NotFound();
        }

        if (await UserManager.GetUserAsync(User) is not { } user) {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(request.OldPassword) || string.IsNullOrWhiteSpace(request.NewPassword)) {
            return BadRequest();
        }

        if (string.Equals(request.NewPassword, request.OldPassword, StringComparison.InvariantCultureIgnoreCase)) {
            return NoContent();
        }

        await UserManager.ChangePasswordAsync(user, request.OldPassword, request.NewPassword);

        return NoContent();
    }

    [Authorize]
    [HttpGet(nameof(Authenticator))]
    public async Task<IActionResult> Authenticator() {
        if (!Options.CurrentValue.AllowTwoFactorAuthentication) {
            return NotFound();
        }

        if (await UserManager.GetUserAsync(User) is not { } user) {
            return NotFound();
        }

        var result = new AuthenticatorResponse {
            IsTwoFactorEnabled = await UserManager.GetTwoFactorEnabledAsync(user),
        };

        if (!result.IsTwoFactorEnabled) {
            await UserManager.ResetAuthenticatorKeyAsync(user);
            var key = await UserManager.GetAuthenticatorKeyAsync(user);
            if (string.IsNullOrEmpty(key)) {
                throw new NotSupportedException("The user manager must produce an authenticator key after reset.");
            }

            var codes = await UserManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);

            result.SharedKey     = key;
            result.RecoveryCodes = codes?.ToArray();
        } else {
            result.IsMachineRemembered = await SignInManager.IsTwoFactorClientRememberedAsync(user);
            result.RecoveryCodesLeft   = await UserManager.CountRecoveryCodesAsync(user);
        }

        return Ok(result);
    }

    [Authorize]
    [HttpPost(nameof(Authenticator))]
    public async Task<IActionResult> Enroll([FromBody] AuthenticatorRequest request) {
        if (!Options.CurrentValue.AllowTwoFactorAuthentication) {
            return NotFound();
        }

        if (await UserManager.GetUserAsync(User) is not { } user) {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(request.TwoFactorCode)) {
            return BadRequest();
        }

        if (!await UserManager.VerifyTwoFactorTokenAsync(user, UserManager.Options.Tokens.AuthenticatorTokenProvider, request.TwoFactorCode))
        {
            return BadRequest();
        }

        await UserManager.SetTwoFactorEnabledAsync(user, true);

        return NoContent();
    }

    [Authorize]
    [HttpPatch(nameof(Authenticator))]
    public async Task<IActionResult> Downgrade([FromBody] AuthenticatorRequest request) {
        if (!Options.CurrentValue.AllowTwoFactorAuthentication) {
            return NotFound();
        }

        if (await UserManager.GetUserAsync(User) is not { } user) {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(request.TwoFactorCode) || string.IsNullOrWhiteSpace(request.TwoFactorRecoveryCode)) {
            return BadRequest();
        }

        var passed = request switch {
            var _ when !string.IsNullOrWhiteSpace(request.TwoFactorCode) => await UserManager.VerifyTwoFactorTokenAsync(user, UserManager.Options.Tokens.AuthenticatorTokenProvider, request.TwoFactorCode),
            var _ when !string.IsNullOrWhiteSpace(request.TwoFactorRecoveryCode) => (await UserManager.RedeemTwoFactorRecoveryCodeAsync(user, request.TwoFactorRecoveryCode)).Succeeded,
            var _ => false,
        };

        if (!passed) {
            return BadRequest();
        }

        await UserManager.SetTwoFactorEnabledAsync(user, false);
        await UserManager.ResetAuthenticatorKeyAsync(user);

        return NoContent();
    }
}
