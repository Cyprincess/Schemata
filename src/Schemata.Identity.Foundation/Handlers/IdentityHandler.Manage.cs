using System;
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
using Schemata.Identity.Skeleton.Claims;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Models;

namespace Schemata.Identity.Foundation.Handlers;

public sealed partial class IdentityHandler<TUser>
    where TUser : SchemataUser, new()
{
    public async Task<IdentityResult<ClaimsStore>> ProfileAsync(
        ClaimsPrincipal   principal,
        CancellationToken ct = default
    ) {
        var ctx = new AdviceContext(_sp);

        switch (await Advisor.For<IIdentityRequestAdvisor<Unit>>()
                             .RunAsync(ctx, Unit.Value, IdentityOperation.Profile, principal, ct)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<IdentityResult<ClaimsStore>>(out var response):
                return response!;
            case AdviseResult.Block:
            default:
                throw new AuthorizationException();
        }

        if (await _users.GetUserAsync(principal) is not { } found) {
            throw new NotFoundException();
        }

        var claims = new ClaimsStore();

        foreach (var claim in principal.Claims) {
            claims.AddClaim(claim.Type, claim.Value);
        }

        switch (await Advisor.For<IIdentityProfileResponseAdvisor<TUser>>()
                             .RunAsync(ctx, found, claims, principal, ct)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<IdentityResult<ClaimsStore>>(out var response):
                return response!;
            case AdviseResult.Block:
            default:
                throw new AuthorizationException();
        }

        return IdentityResult<ClaimsStore>.Success(claims);
    }

    public async Task<IdentityResult<Unit>> ChangeEmailAsync(
        ProfileRequest    request,
        ClaimsPrincipal   principal,
        CancellationToken ct = default
    ) {
        var ctx = new AdviceContext(_sp);

        switch (await Advisor.For<IIdentityRequestAdvisor<ProfileRequest>>()
                             .RunAsync(ctx, request, IdentityOperation.ChangeEmail, principal, ct)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<IdentityResult<Unit>>(out var response):
                return response!;
            case AdviseResult.Block:
            default:
                throw new AuthorizationException();
        }

        if (await _users.GetUserAsync(principal) is not { } found) {
            throw new NotFoundException();
        }

        await SendConfirmationCodeAsync(found, request.EmailAddress, null);

        switch (await Advisor.For<IIdentityProfileChangeAdvisor>()
                             .RunAsync(ctx, found, IdentityOperation.ChangeEmail, ct)) {
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

    public async Task<IdentityResult<Unit>> ChangePhoneAsync(
        ProfileRequest    request,
        ClaimsPrincipal   principal,
        CancellationToken ct = default
    ) {
        var ctx = new AdviceContext(_sp);

        switch (await Advisor.For<IIdentityRequestAdvisor<ProfileRequest>>()
                             .RunAsync(ctx, request, IdentityOperation.ChangePhone, principal, ct)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<IdentityResult<Unit>>(out var response):
                return response!;
            case AdviseResult.Block:
            default:
                throw new AuthorizationException();
        }

        if (await _users.GetUserAsync(principal) is not { } found) {
            throw new NotFoundException();
        }

        await SendConfirmationCodeAsync(found, null, request.PhoneNumber);

        switch (await Advisor.For<IIdentityProfileChangeAdvisor>()
                             .RunAsync(ctx, found, IdentityOperation.ChangePhone, ct)) {
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

    public async Task<IdentityResult<Unit>> ChangePasswordAsync(
        ProfileRequest    request,
        ClaimsPrincipal   principal,
        CancellationToken ct = default
    ) {
        var ctx = new AdviceContext(_sp);

        switch (await Advisor.For<IIdentityRequestAdvisor<ProfileRequest>>()
                             .RunAsync(ctx, request, IdentityOperation.ChangePassword, principal, ct)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<IdentityResult<Unit>>(out var response):
                return response!;
            case AdviseResult.Block:
            default:
                throw new AuthorizationException();
        }

        if (await _users.GetUserAsync(principal) is not { } found) {
            throw new NotFoundException();
        }

        var result = await _users.ChangePasswordAsync(found, request.OldPassword!, request.NewPassword!);
        if (!result.Succeeded) {
            throw new ValidationException(result.Errors.Select(e => new ErrorFieldViolation { Reason = e.Code, Description = e.Description }));
        }

        switch (await Advisor.For<IIdentityProfileChangeAdvisor>()
                             .RunAsync(ctx, found, IdentityOperation.ChangePassword, ct)) {
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

    public async Task<IdentityResult<AuthenticatorResponse>> AuthenticatorAsync(
        ClaimsPrincipal   principal,
        CancellationToken ct = default
    ) {
        var ctx = new AdviceContext(_sp);

        switch (await Advisor.For<IIdentityRequestAdvisor<Unit>>()
                             .RunAsync(ctx, Unit.Value, IdentityOperation.Authenticator, principal, ct)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<IdentityResult<AuthenticatorResponse>>(out var response):
                return response!;
            case AdviseResult.Block:
            default:
                throw new AuthorizationException();
        }

        if (await _users.GetUserAsync(principal) is not { } found) {
            throw new NotFoundException();
        }

        var result = new AuthenticatorResponse { IsTwoFactorEnabled = await _users.GetTwoFactorEnabledAsync(found) };

        if (!result.IsTwoFactorEnabled) {
            await _users.ResetAuthenticatorKeyAsync(found);
            var key = await _users.GetAuthenticatorKeyAsync(found);
            if (string.IsNullOrWhiteSpace(key)) {
                throw new NotSupportedException(SchemataResources.GetResourceString(SchemataResources.ST3001));
            }

            var codes = await _users.GenerateNewTwoFactorRecoveryCodesAsync(found, 10);

            result.SharedKey     = key;
            result.RecoveryCodes = codes?.ToArray();
        } else {
            result.IsMachineRemembered = await _sign.IsTwoFactorClientRememberedAsync(found);
            result.RecoveryCodesLeft   = await _users.CountRecoveryCodesAsync(found);
        }

        return IdentityResult<AuthenticatorResponse>.Success(result);
    }

    public async Task<IdentityResult<Unit>> EnrollAsync(
        AuthenticatorRequest request,
        ClaimsPrincipal      principal,
        CancellationToken    ct = default
    ) {
        var ctx = new AdviceContext(_sp);

        switch (await Advisor.For<IIdentityRequestAdvisor<AuthenticatorRequest>>()
                             .RunAsync(ctx, request, IdentityOperation.Enroll, principal, ct)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<IdentityResult<Unit>>(out var response):
                return response!;
            case AdviseResult.Block:
            default:
                throw new AuthorizationException();
        }

        if (await _users.GetUserAsync(principal) is not { } found) {
            throw new NotFoundException();
        }

        await _users.SetTwoFactorEnabledAsync(found, true);

        switch (await Advisor.For<IIdentity2FaAdvisor>()
                             .RunAsync(ctx, found, IdentityOperation.Enroll, ct)) {
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

    public async Task<IdentityResult<Unit>> DowngradeAsync(
        AuthenticatorRequest request,
        ClaimsPrincipal      principal,
        CancellationToken    ct = default
    ) {
        var ctx = new AdviceContext(_sp);

        switch (await Advisor.For<IIdentityRequestAdvisor<AuthenticatorRequest>>()
                             .RunAsync(ctx, request, IdentityOperation.Downgrade, principal, ct)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<IdentityResult<Unit>>(out var response):
                return response!;
            case AdviseResult.Block:
            default:
                throw new AuthorizationException();
        }

        if (await _users.GetUserAsync(principal) is not { } found) {
            throw new NotFoundException();
        }

        var passed = request switch {
            var _ when !string.IsNullOrWhiteSpace(request.TwoFactorCode) => await _users.VerifyTwoFactorTokenAsync(found, _users.Options.Tokens.AuthenticatorTokenProvider, request.TwoFactorCode),
            var _ when !string.IsNullOrWhiteSpace(request.TwoFactorRecoveryCode) => (await _users.RedeemTwoFactorRecoveryCodeAsync(found, request.TwoFactorRecoveryCode)).Succeeded,
            var _ => false,
        };

        if (!passed) {
            throw new InvalidArgumentException();
        }

        await _users.SetTwoFactorEnabledAsync(found, false);
        await _users.ResetAuthenticatorKeyAsync(found);

        switch (await Advisor.For<IIdentity2FaAdvisor>()
                             .RunAsync(ctx, found, IdentityOperation.Downgrade, ct)) {
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
