using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Schemata.Identity.Skeleton.Models;

namespace Schemata.Identity.Foundation.Controllers;

public sealed partial class AuthenticateController : ControllerBase
{
    [HttpGet(nameof(Confirm))]
    public async Task<IActionResult> Confirm(
        [FromQuery] string? email,
        [FromQuery] string? phone,
        [FromQuery] string? code) {
        if (!options.CurrentValue.AllowAccountConfirmation) {
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
            var _ when !string.IsNullOrWhiteSpace(email) => await userManager.ChangeEmailAsync(user, email, code),
            var _ when !string.IsNullOrWhiteSpace(phone) => await userManager.ChangePhoneNumberAsync(user, phone, code),
            var _                                        => null,
        };

        if (result is not { Succeeded: true }) {
            return BadRequest();
        }

        return NoContent();
    }

    [HttpPost(nameof(Code))]
    public async Task<IActionResult> Code([FromBody] ForgetRequest request) {
        if (!options.CurrentValue.AllowAccountConfirmation) {
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
