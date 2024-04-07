using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Managers;
using Schemata.Identity.Skeleton.Services;

namespace Schemata.Identity.Foundation.Controllers;

[Route("~/[controller]")]
[Produces("application/json")]
public partial class AuthenticateController : ControllerBase
{
    protected readonly IMailSender<SchemataUser>           MailSender;
    protected readonly IMessageSender<SchemataUser>        MessageSender;
    protected readonly IOptionsMonitor<BearerTokenOptions> Options;
    protected readonly SignInManager<SchemataUser>         SignInManager;
    protected readonly SchemataUserManager<SchemataUser>   UserManager;

    public AuthenticateController(
        SignInManager<SchemataUser>         signInManager,
        SchemataUserManager<SchemataUser>   userManager,
        IMailSender<SchemataUser>           mailSender,
        IMessageSender<SchemataUser>        messageSender,
        IOptionsMonitor<BearerTokenOptions> options) {
        SignInManager = signInManager;
        UserManager   = userManager;
        MailSender    = mailSender;
        MessageSender = messageSender;
        Options       = options;
    }

    protected virtual async Task<SchemataUser?> GetUserAsync(string? email, string? phone) {
        return true switch {
            var _ when !string.IsNullOrWhiteSpace(email) => await UserManager.FindByEmailAsync(email),
            var _ when !string.IsNullOrWhiteSpace(phone) => await UserManager.FindByPhoneAsync(phone),
            var _                                        => null,
        };
    }

    protected virtual async Task SendConfirmationCodeAsync(SchemataUser user, string? email, string? phone) {
        switch (user) {
            case var _ when !string.IsNullOrWhiteSpace(email):
            {
                var code = await UserManager.GenerateChangeEmailTokenAsync(user, email);
                await MailSender.SendConfirmationCodeAsync(user, email, code);
                break;
            }
            case var _ when !string.IsNullOrWhiteSpace(phone):
            {
                var code = await UserManager.GenerateChangePhoneNumberTokenAsync(user, phone);
                await MessageSender.SendConfirmationCodeAsync(user, phone, code);
                break;
            }
        }
    }
}
