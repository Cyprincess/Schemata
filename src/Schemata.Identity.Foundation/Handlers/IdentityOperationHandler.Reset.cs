using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Advice;
using Schemata.Identity.Skeleton;
using Schemata.Identity.Skeleton.Advisors;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Services;
using Schemata.Identity.Skeleton.Models;

namespace Schemata.Identity.Foundation.Handlers;

internal sealed partial class IdentityOperationHandler<TUser>
    where TUser : SchemataUser, new()
{
    /// <summary>Sends a password-reset code to a confirmed contact address.</summary>
    public async Task<IdentityResult<Unit>> ForgotAsync(
        ForgetRequest     request,
        ClaimsPrincipal   principal,
        AdviceContext     context,
        CancellationToken ct = default
    ) {

        var execution = new IdentityOperationExecution<IdentityResult<Unit>> {
            Operation = IdentityOperation.Forgot,
            Principal = principal,
        };

        var handled = await RunStageAsync(
            execution,
            () => Advisor.For<IIdentityRequestAdvisor<ForgetRequest, IdentityResult<Unit>>>()
                         .RunAsync(context, request, execution, ct));
        if (handled is not null) {
            return handled;
        }

        var found = await GetUserAsync(request.EmailAddress, request.PhoneNumber);
        if (found is null) {
            throw new NoContentException();
        }

        switch (request) {
            case var _ when !string.IsNullOrWhiteSpace(request.EmailAddress)
                         && await _users.IsEmailConfirmedAsync(found):
            {
                var code   = await _users.GeneratePasswordResetTokenAsync(found);
                var sender = _services.GetRequiredService<IMailSender<TUser>>();
                await sender.SendPasswordResetCodeAsync(found, request.EmailAddress, code);
                break;
            }
            case var _ when !string.IsNullOrWhiteSpace(request.PhoneNumber)
                         && await _users.IsPhoneNumberConfirmedAsync(found):
            {
                var code   = await _users.GeneratePasswordResetTokenAsync(found);
                var sender = _services.GetRequiredService<IMessageSender<TUser>>();
                await sender.SendPasswordResetCodeAsync(found, request.PhoneNumber, code);
                break;
            }
        }

        return IdentityResult<Unit>.Success(null);
    }

    /// <summary>Resets a password with a password-reset code.</summary>
    public async Task<IdentityResult<Unit>> ResetAsync(
        ResetRequest      request,
        ClaimsPrincipal   principal,
        AdviceContext     context,
        CancellationToken ct = default
    ) {

        var execution = new IdentityOperationExecution<IdentityResult<Unit>> {
            Operation = IdentityOperation.Reset,
            Principal = principal,
        };

        var handled = await RunStageAsync(
            execution,
            () => Advisor.For<IIdentityRequestAdvisor<ResetRequest, IdentityResult<Unit>>>()
                         .RunAsync(context, request, execution, ct));
        if (handled is not null) {
            return handled;
        }

        // Unauthenticated reset and confirm flows share one failure shape for missing accounts,
        // unconfirmed contacts, and invalid tokens. Success is observable through the delivered
        // reset code.
        var found = await GetUserAsync(request.EmailAddress, request.PhoneNumber);
        if (found is null) {
            throw new NoContentException();
        }

        var confirmed = request switch {
            var _ when !string.IsNullOrWhiteSpace(request.EmailAddress) => await _users.IsEmailConfirmedAsync(found),
            var _ when !string.IsNullOrWhiteSpace(request.PhoneNumber) =>
                await _users.IsPhoneNumberConfirmedAsync(found),
            var _ => false,
        };

        if (!confirmed) {
            throw new NoContentException();
        }

        var result = await _users.ResetPasswordAsync(found, request.Code, request.Password);
        if (!result.Succeeded) {
            throw new NoContentException();
        }

        handled = await RunStageAsync(
            execution,
            () => Advisor.For<IIdentityRecoveryAdvisor>()
                         .RunAsync(context, found, IdentityOperation.Reset, execution, ct));
        if (handled is not null) {
            return handled;
        }

        return IdentityResult<Unit>.Success(null);
    }
}
