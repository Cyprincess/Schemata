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
using Schemata.Common.Errors;
using Schemata.Identity.Skeleton;
using Schemata.Identity.Skeleton.Advisors;
using Schemata.Identity.Skeleton.Claims;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Models;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Identity.Foundation.Handlers;

public sealed partial class IdentityHandler<TUser>
    where TUser : SchemataUser, new()
{
    /// <summary>Builds the authenticated user's profile claims.</summary>
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
                throw new PermissionDeniedException();
        }

        if (await _users.GetUserAsync(principal) is not { } found) {
            throw SchemataResourceErrors.NotFound<TUser>(principal.FindFirstValue(Claims.Subject));
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
                throw new PermissionDeniedException();
        }

        return IdentityResult<ClaimsStore>.Success(claims);
    }

    /// <summary>Sends an email-change confirmation code for the authenticated user.</summary>
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
                throw new PermissionDeniedException();
        }

        if (await _users.GetUserAsync(principal) is not { } found) {
            throw SchemataResourceErrors.NotFound<TUser>(principal.FindFirstValue(Claims.Subject));
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
                throw new PermissionDeniedException();
        }

        return IdentityResult<Unit>.Success(null);
    }

    /// <summary>Sends a phone-change confirmation code for the authenticated user.</summary>
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
                throw new PermissionDeniedException();
        }

        if (await _users.GetUserAsync(principal) is not { } found) {
            throw SchemataResourceErrors.NotFound<TUser>(principal.FindFirstValue(Claims.Subject));
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
                throw new PermissionDeniedException();
        }

        return IdentityResult<Unit>.Success(null);
    }

    /// <summary>Changes the authenticated user's password.</summary>
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
                throw new PermissionDeniedException();
        }

        if (await _users.GetUserAsync(principal) is not { } found) {
            throw SchemataResourceErrors.NotFound<TUser>(principal.FindFirstValue(Claims.Subject));
        }

        var result = await _users.ChangePasswordAsync(found, request.OldPassword!, request.NewPassword!);
        if (!result.Succeeded) {
            throw new ValidationException(result.Errors.Select(e => new ErrorFieldViolation {
                Reason      = NormalizeIdentityCode(e.Code),
                Description = e.Description,
            }));
        }

        switch (await Advisor.For<IIdentityProfileChangeAdvisor>()
                             .RunAsync(ctx, found, IdentityOperation.ChangePassword, ct)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<IdentityResult<Unit>>(out var response):
                return response!;
            case AdviseResult.Block:
            default:
                throw new PermissionDeniedException();
        }

        return IdentityResult<Unit>.Success(null);
    }

    /// <summary>Builds two-factor authenticator enrollment state for the authenticated user.</summary>
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
                throw new PermissionDeniedException();
        }

        if (await _users.GetUserAsync(principal) is not { } found) {
            throw SchemataResourceErrors.NotFound<TUser>(principal.FindFirstValue(Claims.Subject));
        }

        var result = new AuthenticatorResponse { IsTwoFactorEnabled = await _users.GetTwoFactorEnabledAsync(found) };

        if (!result.IsTwoFactorEnabled) {
            await _users.ResetAuthenticatorKeyAsync(found);
            var key = await _users.GetAuthenticatorKeyAsync(found);
            if (string.IsNullOrWhiteSpace(key)) {
                throw new NotSupportedException(SchemataResources.GetResourceString(SchemataResources.AUTHENTICATOR_KEY_REQUIRED));
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

    /// <summary>Enables two-factor authenticator sign-in for the authenticated user.</summary>
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
                throw new PermissionDeniedException();
        }

        if (await _users.GetUserAsync(principal) is not { } found) {
            throw SchemataResourceErrors.NotFound<TUser>(principal.FindFirstValue(Claims.Subject));
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
                throw new PermissionDeniedException();
        }

        return IdentityResult<Unit>.Success(null);
    }

    /// <summary>Disables two-factor authenticator sign-in for the authenticated user.</summary>
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
                throw new PermissionDeniedException();
        }

        if (await _users.GetUserAsync(principal) is not { } found) {
            throw SchemataResourceErrors.NotFound<TUser>(principal.FindFirstValue(Claims.Subject));
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
                throw new PermissionDeniedException();
        }

        return IdentityResult<Unit>.Success(null);
    }
}
