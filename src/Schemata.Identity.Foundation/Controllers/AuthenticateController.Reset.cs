using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Schemata.Identity.Skeleton.Models;

namespace Schemata.Identity.Foundation.Controllers;

public sealed partial class AuthenticateController : ControllerBase
{
    /// <summary>
    ///     Initiates the password reset flow by sending a reset code to the user's confirmed contact.
    /// </summary>
    /// <param name="request">The forget request identifying the user by email or phone.</param>
    /// <returns>202 Accepted regardless of whether the user exists, to prevent user enumeration.</returns>
    [HttpPost(nameof(Forgot))]
    public async Task<IActionResult> Forgot([FromBody] ForgetRequest request) {
        if (!_options.CurrentValue.AllowPasswordReset) {
            return NotFound();
        }

        var user = await GetUserAsync(request.EmailAddress, request.PhoneNumber);
        if (user is null) {
            return Accepted();
        }

        switch (request) {
            case var _ when !string.IsNullOrWhiteSpace(request.EmailAddress)
                         && await _userManager.IsEmailConfirmedAsync(user):
            {
                var code = await _userManager.GeneratePasswordResetTokenAsync(user);
                await _mailSender.SendPasswordResetCodeAsync(user, request.EmailAddress, code);
                break;
            }
            case var _ when !string.IsNullOrWhiteSpace(request.PhoneNumber)
                         && await _userManager.IsPhoneNumberConfirmedAsync(user):
            {
                var code = await _userManager.GeneratePasswordResetTokenAsync(user);
                await _messageSender.SendPasswordResetCodeAsync(user, request.PhoneNumber, code);
                break;
            }
        }

        return Accepted();
    }

    /// <summary>
    ///     Resets the user's password using a previously issued reset code.
    /// </summary>
    /// <param name="request">The reset request containing the code and new password.</param>
    [HttpPost(nameof(Reset))]
    public async Task<IActionResult> Reset([FromBody] ResetRequest request) {
        if (!_options.CurrentValue.AllowPasswordReset) {
            return NotFound();
        }

        var user = await GetUserAsync(request.EmailAddress, request.PhoneNumber);
        if (user is null) {
            return BadRequest();
        }

        var confirmed = request switch {
            var _ when !string.IsNullOrWhiteSpace(request.EmailAddress) => await _userManager.IsEmailConfirmedAsync(user),
            var _ when !string.IsNullOrWhiteSpace(request.PhoneNumber)  => await _userManager.IsPhoneNumberConfirmedAsync(user),
            var _ => false,
        };

        if (!confirmed) {
            return BadRequest();
        }

        var result = await _userManager.ResetPasswordAsync(user, request.Code, request.Password);
        if (!result.Succeeded) {
            return BadRequest(result.Errors);
        }

        return NoContent();
    }
}
