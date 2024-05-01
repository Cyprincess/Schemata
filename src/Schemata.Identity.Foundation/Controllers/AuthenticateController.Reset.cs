using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Schemata.Identity.Skeleton.Models;

namespace Schemata.Identity.Foundation.Controllers;

public sealed partial class AuthenticateController : ControllerBase
{
    [HttpPost(nameof(Forgot))]
    public async Task<IActionResult> Forgot([FromBody] ForgetRequest request) {
        if (!options.CurrentValue.AllowPasswordReset) {
            return NotFound();
        }

        var user = await GetUserAsync(request.EmailAddress, request.PhoneNumber);
        if (user is null) {
            return Accepted();
        }

        switch (request) {
            case var _ when !string.IsNullOrWhiteSpace(request.EmailAddress)
                         && await userManager.IsEmailConfirmedAsync(user):
            {
                var code = await userManager.GeneratePasswordResetTokenAsync(user);
                await mailSender.SendPasswordResetCodeAsync(user, request.EmailAddress, code);
                break;
            }
            case var _ when !string.IsNullOrWhiteSpace(request.PhoneNumber)
                         && await userManager.IsPhoneNumberConfirmedAsync(user):
            {
                var code = await userManager.GeneratePasswordResetTokenAsync(user);
                await messageSender.SendPasswordResetCodeAsync(user, request.PhoneNumber, code);
                break;
            }
        }

        return Accepted();
    }

    [HttpPost(nameof(Reset))]
    public async Task<IActionResult> Reset([FromBody] ResetRequest request) {
        if (!options.CurrentValue.AllowPasswordReset) {
            return NotFound();
        }

        var user = await GetUserAsync(request.EmailAddress, request.PhoneNumber);
        if (user is null) {
            return BadRequest();
        }

        var confirmed = request switch {
            var _ when !string.IsNullOrWhiteSpace(request.EmailAddress) => await userManager.IsEmailConfirmedAsync(user),
            var _ when !string.IsNullOrWhiteSpace(request.PhoneNumber)  => await userManager.IsPhoneNumberConfirmedAsync(user),
            var _                                                       => false,
        };

        if (!confirmed) {
            return BadRequest();
        }

        var result = await userManager.ResetPasswordAsync(user, request.Code, request.Password);
        if (!result.Succeeded) {
            return BadRequest(result.Errors);
        }

        return NoContent();
    }
}
