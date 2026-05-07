using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Managers;
using Schemata.Identity.Skeleton.Services;

namespace Schemata.Identity.Foundation.Handlers;

public sealed partial class IdentityHandler<TUser>
    where TUser : SchemataUser, new()
{
    private readonly IMailSender<TUser>         _mail;
    private readonly IMessageSender<TUser>      _message;
    private readonly SignInManager<TUser>       _sign;
    private readonly IServiceProvider           _sp;
    private readonly SchemataUserManager<TUser> _users;

    public IdentityHandler(
        SchemataUserManager<TUser> users,
        SignInManager<TUser>       sign,
        IMailSender<TUser>         mail,
        IMessageSender<TUser>      message,
        IServiceProvider           sp
    ) {
        _users   = users;
        _sign    = sign;
        _mail    = mail;
        _message = message;
        _sp      = sp;
    }

    private async Task<TUser?> GetUserAsync(string? email, string? phone) {
        return true switch {
            var _ when !string.IsNullOrWhiteSpace(email) => await _users.FindByEmailAsync(email),
            var _ when !string.IsNullOrWhiteSpace(phone) => await _users.FindByPhoneAsync(phone),
            var _                                        => null,
        };
    }

    private async Task SendConfirmationCodeAsync(TUser user, string? email, string? phone) {
        switch (user) {
            case var _ when !string.IsNullOrWhiteSpace(email):
            {
                var code = await _users.GenerateChangeEmailTokenAsync(user, email);
                await _mail.SendConfirmationCodeAsync(user, email, code);
                break;
            }
            case var _ when !string.IsNullOrWhiteSpace(phone):
            {
                var code = await _users.GenerateChangePhoneNumberTokenAsync(user, phone);
                await _message.SendConfirmationCodeAsync(user, phone, code);
                break;
            }
        }
    }
}
