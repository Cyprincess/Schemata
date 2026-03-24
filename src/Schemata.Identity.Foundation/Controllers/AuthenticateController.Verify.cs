using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Schemata.Identity.Skeleton.Models;

namespace Schemata.Identity.Foundation.Controllers;

public sealed partial class AuthenticateController : ControllerBase
{
    /// <summary>
    ///     Confirms an email address or phone number change using a verification code.
    /// </summary>
    /// <param name="email">The email address to confirm.</param>
    /// <param name="phone">The phone number to confirm.</param>
    /// <param name="code">The verification code.</param>
    [HttpGet(nameof(Confirm))]
    public async Task<IActionResult> Confirm(
        [FromQuery] string? email,
        [FromQuery] string? phone,
        [FromQuery] string? code
    ) {
        if (!_options.CurrentValue.AllowAccountConfirmation) {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(code)) {
            return BadRequest();
        }

        if (string.IsNullOrWhiteSpace(email) && string.IsNullOrWhiteSpace(phone)) {
            return BadRequest();
        }

        var user = await GetUserAsync(email, phone);
        if (user is null) {
            return BadRequest();
        }

        var result = code switch {
            var _ when !string.IsNullOrWhiteSpace(email) => await _userManager.ChangeEmailAsync(user, email, code),
            var _ when !string.IsNullOrWhiteSpace(phone) => await _userManager.ChangePhoneNumberAsync(user, phone, code),
            var _ => null,
        };

        if (result is not { Succeeded: true }) {
            return BadRequest();
        }

        return NoContent();
    }

    /// <summary>
    ///     Sends a new confirmation code to the user's email or phone.
    /// </summary>
    /// <param name="request">The request identifying the contact to send the code to.</param>
    /// <returns>202 Accepted regardless of whether the user exists, to prevent user enumeration.</returns>
    [HttpPost(nameof(Code))]
    public async Task<IActionResult> Code([FromBody] ForgetRequest request) {
        if (!_options.CurrentValue.AllowAccountConfirmation) {
            return NotFound();
        }

        var user = await GetUserAsync(request.EmailAddress, request.PhoneNumber);
        if (user is null) {
            return Accepted();
        }

        await SendConfirmationCodeAsync(user, request.EmailAddress, request.PhoneNumber);

        return Accepted();
    }
}
