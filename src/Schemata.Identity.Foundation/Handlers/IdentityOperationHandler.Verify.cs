using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Advice;
using Schemata.Identity.Skeleton;
using Schemata.Identity.Skeleton.Advisors;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Models;

namespace Schemata.Identity.Foundation.Handlers;

internal sealed partial class IdentityOperationHandler<TUser>
    where TUser : SchemataUser, new()
{
    /// <summary>Confirms an email address or phone number with a confirmation code.</summary>
    public async Task<IdentityResult<Unit>> ConfirmAsync(
        ConfirmRequest    request,
        ClaimsPrincipal   principal,
        AdviceContext     context,
        CancellationToken ct = default
    ) {

        var execution = new IdentityOperationExecution<IdentityResult<Unit>> {
            Operation = IdentityOperation.Confirm,
            Principal = principal,
        };

        var handled = await RunStageAsync(
            execution,
            () => Advisor.For<IIdentityRequestAdvisor<ConfirmRequest, IdentityResult<Unit>>>()
                         .RunAsync(context, request, execution, ct));
        if (handled is not null) {
            return handled;
        }

        // A signed-in initiator confirms against their own account: the change code binds the
        // initiating user and the requested contact through the UserManager's purpose-specific
        // verification, never a contact-side lookup that could resolve a different account.
        // Anonymous callers keep the current-contact confirmation path, whose uniform
        // missing-account/invalid-code shape resists account enumeration.
        var found = principal.Identity is ClaimsIdentity { IsAuthenticated: true }
            ? await _users.GetUserAsync(principal)
            : await GetUserAsync(request.EmailAddress, request.PhoneNumber);
        if (found is null) {
            throw new NoContentException();
        }

        var result = request switch {
            var _ when !string.IsNullOrWhiteSpace(request.EmailAddress) => await _users.ChangeEmailAsync(found, request.EmailAddress, request.Code),
            var _ when !string.IsNullOrWhiteSpace(request.PhoneNumber) => await _users.ChangePhoneNumberAsync(found, request.PhoneNumber, request.Code),
            var _ => null,
        };

        if (result is not { Succeeded: true }) {
            throw new NoContentException();
        }

        handled = await RunStageAsync(
            execution,
            () => Advisor.For<IIdentityRecoveryAdvisor>()
                         .RunAsync(context, found, IdentityOperation.Confirm, execution, ct));
        if (handled is not null) {
            return handled;
        }

        return IdentityResult<Unit>.Success(null);
    }

    /// <summary>Sends an account-confirmation code to a contact address.</summary>
    public async Task<IdentityResult<Unit>> CodeAsync(
        ForgetRequest     request,
        ClaimsPrincipal   principal,
        AdviceContext     context,
        CancellationToken ct = default
    ) {

        var execution = new IdentityOperationExecution<IdentityResult<Unit>> {
            Operation = IdentityOperation.Code,
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

        await SendConfirmationCodeAsync(found, request.EmailAddress, request.PhoneNumber);

        return IdentityResult<Unit>.Success(null);
    }
}
