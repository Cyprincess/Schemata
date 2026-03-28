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

public sealed partial class IdentityHandler<TUser>
    where TUser : SchemataUser, new()
{
    public async Task<IdentityResult<Unit>> ConfirmAsync(
        ConfirmRequest    request,
        ClaimsPrincipal   principal,
        CancellationToken ct = default
    ) {
        var ctx = new AdviceContext(_sp);

        switch (await Advisor.For<IIdentityRequestAdvisor<ConfirmRequest>>()
                             .RunAsync(ctx, request, IdentityOperation.Confirm, principal, ct)) {
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

        var result = request switch {
            var _ when !string.IsNullOrWhiteSpace(request.EmailAddress) => await _users.ChangeEmailAsync(found, request.EmailAddress, request.Code),
            var _ when !string.IsNullOrWhiteSpace(request.PhoneNumber) => await _users.ChangePhoneNumberAsync(found, request.PhoneNumber, request.Code),
            var _ => null,
        };

        if (result is not { Succeeded: true }) {
            throw new InvalidArgumentException();
        }

        switch (await Advisor.For<IIdentityRecoveryAdvisor>()
                             .RunAsync(ctx, found, IdentityOperation.Confirm, ct)) {
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

    public async Task<IdentityResult<Unit>> CodeAsync(
        ForgetRequest     request,
        ClaimsPrincipal   principal,
        CancellationToken ct = default
    ) {
        var ctx = new AdviceContext(_sp);

        switch (await Advisor.For<IIdentityRequestAdvisor<ForgetRequest>>()
                             .RunAsync(ctx, request, IdentityOperation.Code, principal, ct)) {
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

        await SendConfirmationCodeAsync(found, request.EmailAddress, request.PhoneNumber);

        return IdentityResult<Unit>.Success(null);
    }
}
