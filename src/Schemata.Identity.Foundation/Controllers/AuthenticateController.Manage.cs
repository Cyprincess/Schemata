using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Models;

namespace Schemata.Identity.Foundation.Controllers;

public sealed partial class AuthenticateController : ControllerBase
{
    /// <summary>
    ///     Returns the current user's profile as a claims store.
    /// </summary>
    [Authorize]
    [HttpGet("~/Account/Profile")]
    public async Task<IActionResult> Profile() {
        if (await _userManager.GetUserAsync(User) is not { } user) {
            return NotFound();
        }

        var store = await _userManager.ToClaimsAsync(user);

        return Ok(store);
    }

    /// <summary>
    ///     Initiates an email address change by sending a confirmation code.
    /// </summary>
    /// <param name="request">The profile request containing the new email address.</param>
    [Authorize]
    [HttpPut("~/Account/Profile/Email")]
    public async Task<IActionResult> Email([FromBody] ProfileRequest request) {
        if (!_options.CurrentValue.AllowEmailChange) {
            return NotFound();
        }

        if (await _userManager.GetUserAsync(User) is not { } user) {
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

    /// <summary>
    ///     Initiates a phone number change by sending a confirmation code.
    /// </summary>
    /// <param name="request">The profile request containing the new phone number.</param>
    [Authorize]
    [HttpPut("~/Account/Profile/Phone")]
    public async Task<IActionResult> Phone([FromBody] ProfileRequest request) {
        if (!_options.CurrentValue.AllowPhoneNumberChange) {
            return NotFound();
        }

        if (await _userManager.GetUserAsync(User) is not { } user) {
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

    /// <summary>
    ///     Changes the current user's password.
    /// </summary>
    /// <param name="request">The profile request containing old and new passwords.</param>
    [Authorize]
    [HttpPut("~/Account/Profile/Password")]
    public async Task<IActionResult> Password([FromBody] ProfileRequest request) {
        if (!_options.CurrentValue.AllowPasswordChange) {
            return NotFound();
        }

        if (await _userManager.GetUserAsync(User) is not { } user) {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(request.OldPassword) || string.IsNullOrWhiteSpace(request.NewPassword)) {
            return BadRequest();
        }

        if (string.Equals(request.NewPassword, request.OldPassword, StringComparison.InvariantCultureIgnoreCase)) {
            return NoContent();
        }

        await _userManager.ChangePasswordAsync(user, request.OldPassword, request.NewPassword);

        return NoContent();
    }

    /// <summary>
    ///     Returns the current two-factor authenticator status, generating a new shared key if not yet enabled.
    /// </summary>
    [Authorize]
    [HttpGet(nameof(Authenticator))]
    public async Task<IActionResult> Authenticator() {
        if (!_options.CurrentValue.AllowTwoFactorAuthentication) {
            return NotFound();
        }

        if (await _userManager.GetUserAsync(User) is not { } user) {
            return NotFound();
        }

        var signInManager = _sp.GetRequiredService<SignInManager<SchemataUser>>();

        var result = new AuthenticatorResponse {
            IsTwoFactorEnabled = await _userManager.GetTwoFactorEnabledAsync(user),
        };

        if (!result.IsTwoFactorEnabled) {
            await _userManager.ResetAuthenticatorKeyAsync(user);
            var key = await _userManager.GetAuthenticatorKeyAsync(user);
            if (string.IsNullOrWhiteSpace(key)) {
                throw new NotSupportedException("The user manager must produce an authenticator key after reset.");
            }

            var codes = await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);

            result.SharedKey     = key;
            result.RecoveryCodes = codes?.ToArray();
        } else {
            result.IsMachineRemembered = await signInManager.IsTwoFactorClientRememberedAsync(user);
            result.RecoveryCodesLeft   = await _userManager.CountRecoveryCodesAsync(user);
        }

        return Ok(result);
    }

    /// <summary>
    ///     Enrolls the user in two-factor authentication by verifying a TOTP code.
    /// </summary>
    /// <param name="request">The authenticator request containing the verification code.</param>
    [Authorize]
    [HttpPost(nameof(Authenticator))]
    public async Task<IActionResult> Enroll([FromBody] AuthenticatorRequest request) {
        if (!_options.CurrentValue.AllowTwoFactorAuthentication) {
            return NotFound();
        }

        if (await _userManager.GetUserAsync(User) is not { } user) {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(request.TwoFactorCode)) {
            return BadRequest();
        }

        if (!await _userManager.VerifyTwoFactorTokenAsync(user, _userManager.Options.Tokens.AuthenticatorTokenProvider, request.TwoFactorCode)) {
            return BadRequest();
        }

        await _userManager.SetTwoFactorEnabledAsync(user, true);

        return NoContent();
    }

    /// <summary>
    ///     Disables two-factor authentication after verifying a TOTP or recovery code.
    /// </summary>
    /// <param name="request">The authenticator request containing the verification or recovery code.</param>
    [Authorize]
    [HttpPatch(nameof(Authenticator))]
    public async Task<IActionResult> Downgrade([FromBody] AuthenticatorRequest request) {
        if (!_options.CurrentValue.AllowTwoFactorAuthentication) {
            return NotFound();
        }

        if (await _userManager.GetUserAsync(User) is not { } user) {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(request.TwoFactorCode)
         || string.IsNullOrWhiteSpace(request.TwoFactorRecoveryCode)) {
            return BadRequest();
        }

        var passed = request switch {
            var _ when !string.IsNullOrWhiteSpace(request.TwoFactorCode) => await _userManager.VerifyTwoFactorTokenAsync(user, _userManager.Options.Tokens.AuthenticatorTokenProvider, request.TwoFactorCode),
            var _ when !string.IsNullOrWhiteSpace(request.TwoFactorRecoveryCode) => (await _userManager.RedeemTwoFactorRecoveryCodeAsync(user, request.TwoFactorRecoveryCode)).Succeeded,
            var _ => false,
        };

        if (!passed) {
            return BadRequest();
        }

        await _userManager.SetTwoFactorEnabledAsync(user, false);
        await _userManager.ResetAuthenticatorKeyAsync(user);

        return NoContent();
    }
}
