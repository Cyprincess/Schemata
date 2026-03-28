using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
using Schemata.Advice;
using Schemata.Identity.Skeleton;
using Schemata.Identity.Skeleton.Advisors;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Models;

namespace Schemata.Identity.Foundation.Handlers;

public sealed partial class IdentityHandler<TUser>
    where TUser : SchemataUser, new()
{
    public async Task<IdentityResult<Unit>> ForgotAsync(
        ForgetRequest     request,
        ClaimsPrincipal   principal,
        CancellationToken ct = default
    ) {
        var ctx = new AdviceContext(_sp);

        switch (await Advisor.For<IIdentityRequestAdvisor<ForgetRequest>>()
                             .RunAsync(ctx, request, IdentityOperation.Forgot, principal, ct)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<IdentityResult<Unit>>(out var response):
                return response!;
            case AdviseResult.Block:
            default:
                throw new AuthorizationException();
        }

        var found = await GetUserAsync(request.EmailAddress, request.PhoneNumber);
        if (found is null) {
            throw new NoContentException();
        }

        switch (request) {
            case var _ when !string.IsNullOrWhiteSpace(request.EmailAddress)
                         && await _users.IsEmailConfirmedAsync(found):
            {
                var code = await _users.GeneratePasswordResetTokenAsync(found);
                await _mail.SendPasswordResetCodeAsync(found, request.EmailAddress, code);
                break;
            }
            case var _ when !string.IsNullOrWhiteSpace(request.PhoneNumber)
                         && await _users.IsPhoneNumberConfirmedAsync(found):
            {
                var code = await _users.GeneratePasswordResetTokenAsync(found);
                await _message.SendPasswordResetCodeAsync(found, request.PhoneNumber, code);
                break;
            }
        }

        return IdentityResult<Unit>.Success(null);
    }

    public async Task<IdentityResult<Unit>> ResetAsync(
        ResetRequest      request,
        ClaimsPrincipal   principal,
        CancellationToken ct = default
    ) {
        var ctx = new AdviceContext(_sp);

        switch (await Advisor.For<IIdentityRequestAdvisor<ResetRequest>>()
                             .RunAsync(ctx, request, IdentityOperation.Reset, principal, ct)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<IdentityResult<Unit>>(out var response):
                return response!;
            case AdviseResult.Block:
            default:
                throw new AuthorizationException();
        }

        var found = await GetUserAsync(request.EmailAddress, request.PhoneNumber);
        if (found is null) {
            throw new NotFoundException();
        }

        var confirmed = request switch {
            var _ when !string.IsNullOrWhiteSpace(request.EmailAddress) => await _users.IsEmailConfirmedAsync(found),
            var _ when !string.IsNullOrWhiteSpace(request.PhoneNumber) =>
                await _users.IsPhoneNumberConfirmedAsync(found),
            var _ => false,
        };

        if (!confirmed) {
            throw new InvalidArgumentException();
        }

        var result = await _users.ResetPasswordAsync(found, request.Code, request.Password);
        if (!result.Succeeded) {
            throw new ValidationException(result.Errors.Select(e => new ErrorFieldViolation { Reason = e.Code, Description = e.Description }));
        }

        switch (await Advisor.For<IIdentityRecoveryAdvisor>()
                             .RunAsync(ctx, found, IdentityOperation.Reset, ct)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<IdentityResult<Unit>>(out var response):
                return response!;
            case AdviseResult.Block:
            default:
                throw new AuthorizationException();
        }

        return IdentityResult<Unit>.Success(null);
    }
}
