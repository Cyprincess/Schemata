using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Identity.Skeleton;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Managers;
using Schemata.Identity.Skeleton.Services;

namespace Schemata.Identity.Foundation.Handlers;

/// <summary>Coordinates identity operations with ASP.NET Core Identity managers and Schemata advisors.</summary>
/// <typeparam name="TUser">User entity type handled by the identity manager.</typeparam>
internal sealed partial class IdentityOperationHandler<TUser>
    where TUser : SchemataUser, new()
{
    private readonly IEnumerable<Skeleton.IHostSignInObserver> _hostObservers;

    private readonly IServiceProvider                _services;
    private readonly SignInManager<TUser>            _sign;
    private readonly TimeProvider                    _time;
    private readonly SchemataUserManager<TUser>      _users;

    /// <summary>Creates an identity operation handler.</summary>
    /// <param name="users">User manager for account operations.</param>
    /// <param name="sign">Sign-in manager for credential operations.</param>
    /// <param name="services">Service provider for channel senders.</param>
    /// <param name="time">Clock for authentication timestamps; defaults to the system clock.</param>
    /// <param name="hostObservers">Observers of host authentication renewal.</param>
    public IdentityOperationHandler(
        SchemataUserManager<TUser>                 users,
        SignInManager<TUser>                       sign,
        IServiceProvider                           services,
        TimeProvider?                              time          = null,
        IEnumerable<Skeleton.IHostSignInObserver>? hostObservers = null
    ) {
        _users         = users;
        _sign          = sign;
        _services      = services;
        _time          = time ?? TimeProvider.System;
        _hostObservers = hostObservers ?? [];
    }

    /// <summary>
    ///     Runs one advisor stage over the operation's single carrier. A stage that continues
    ///     leaves no handled response, so a response written without
    ///     <see cref="AdviseResult.Handle" /> cannot reach a later stage of the same operation.
    /// </summary>
    private static async Task<IdentityResult<TResponse>?> RunStageAsync<TResponse>(
        IdentityOperationExecution<IdentityResult<TResponse>> execution,
        Func<Task<AdviseResult>>                              advise
    ) where TResponse : class {
        var handled = await HandledResponseRunner.RunAsync(
            execution, advise, static () => new PermissionDeniedException());
        if (handled is null) {
            execution.Response = null;
        }

        return handled;
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
                // Delivery channels are optional capabilities: the sender resolves only on the
                // operation path that needs it, and an unconfigured channel fails at its use
                // instead of reporting a fabricated delivery.
                var sender = _services.GetRequiredService<IMailSender<TUser>>();
                var code   = await _users.GenerateChangeEmailTokenAsync(user, email);
                await sender.SendConfirmationCodeAsync(user, email, code);
                break;
            }
            case var _ when !string.IsNullOrWhiteSpace(phone):
            {
                var sender = _services.GetRequiredService<IMessageSender<TUser>>();
                var code   = await _users.GenerateChangePhoneNumberTokenAsync(user, phone);
                await sender.SendConfirmationCodeAsync(user, phone, code);
                break;
            }
        }
    }
}
