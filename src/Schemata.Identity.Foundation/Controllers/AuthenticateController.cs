using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Options;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Managers;
using Schemata.Identity.Skeleton.Services;

namespace Schemata.Identity.Foundation.Controllers;

[ApiController]
[Route("~/[controller]")]
public sealed partial class AuthenticateController(
    SignInManager<SchemataUser>              signInManager,
    SchemataUserManager<SchemataUser>        userManager,
    IMailSender<SchemataUser>                mailSender,
    IMessageSender<SchemataUser>             messageSender,
    IOptionsMonitor<BearerTokenOptions>      bearerToken,
    IOptionsMonitor<SchemataIdentityOptions> options) : ControllerBase
{
    private EmptyResult EmptyResult { get; } = new();

    private async Task<SchemataUser?> GetUserAsync(string? email, string? phone) {
        return true switch {
            var _ when !string.IsNullOrWhiteSpace(email) => await userManager.FindByEmailAsync(email),
            var _ when !string.IsNullOrWhiteSpace(phone) => await userManager.FindByPhoneAsync(phone),
            var _                                        => null,
        };
    }

    private async Task SendConfirmationCodeAsync(SchemataUser user, string? email, string? phone) {
        if (!options.CurrentValue.AllowAccountConfirmation) {
            return;
        }

        switch (user) {
            case var _ when !string.IsNullOrWhiteSpace(email):
            {
                var code = await userManager.GenerateChangeEmailTokenAsync(user, email);
                await mailSender.SendConfirmationCodeAsync(user, email, code);
                break;
            }
            case var _ when !string.IsNullOrWhiteSpace(phone):
            {
                var code = await userManager.GenerateChangePhoneNumberTokenAsync(user, phone);
                await messageSender.SendConfirmationCodeAsync(user, phone, code);
                break;
            }
        }
    }
}
