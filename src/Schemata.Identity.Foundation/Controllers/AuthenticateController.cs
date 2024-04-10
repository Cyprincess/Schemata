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
public sealed partial class AuthenticateController : ControllerBase
{
    private readonly IOptionsMonitor<BearerTokenOptions>      _bearerToken;
    private readonly IMailSender<SchemataUser>                _mailSender;
    private readonly IMessageSender<SchemataUser>             _messageSender;
    private readonly IOptionsMonitor<SchemataIdentityOptions> _options;
    private readonly SignInManager<SchemataUser>              _signInManager;
    private readonly SchemataUserManager<SchemataUser>        _userManager;

    public AuthenticateController(
        SignInManager<SchemataUser>              signInManager,
        SchemataUserManager<SchemataUser>        userManager,
        IMailSender<SchemataUser>                mailSender,
        IMessageSender<SchemataUser>             messageSender,
        IOptionsMonitor<BearerTokenOptions>      bearerToken,
        IOptionsMonitor<SchemataIdentityOptions> options) {
        _signInManager = signInManager;
        _userManager   = userManager;
        _mailSender    = mailSender;
        _messageSender = messageSender;
        _bearerToken   = bearerToken;
        _options       = options;
    }

    private EmptyResult EmptyResult { get; } = new();

    private async Task<SchemataUser?> GetUserAsync(string? email, string? phone) {
        return true switch {
            var _ when !string.IsNullOrWhiteSpace(email) => await _userManager.FindByEmailAsync(email),
            var _ when !string.IsNullOrWhiteSpace(phone) => await _userManager.FindByPhoneAsync(phone),
            var _                                        => null,
        };
    }

    private async Task SendConfirmationCodeAsync(SchemataUser user, string? email, string? phone) {
        if (!_options.CurrentValue.AllowAccountConfirmation) {
            return;
        }

        switch (user) {
            case var _ when !string.IsNullOrWhiteSpace(email):
            {
                var code = await _userManager.GenerateChangeEmailTokenAsync(user, email);
                await _mailSender.SendConfirmationCodeAsync(user, email, code);
                break;
            }
            case var _ when !string.IsNullOrWhiteSpace(phone):
            {
                var code = await _userManager.GenerateChangePhoneNumberTokenAsync(user, phone);
                await _messageSender.SendConfirmationCodeAsync(user, phone, code);
                break;
            }
        }
    }
}
