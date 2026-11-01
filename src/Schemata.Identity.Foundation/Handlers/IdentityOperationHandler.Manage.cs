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

internal sealed partial class IdentityOperationHandler<TUser>
    where TUser : SchemataUser, new()
{
    /// <summary>Builds the authenticated user's profile claims.</summary>
    public async Task<IdentityResult<ClaimsStore>> ProfileAsync(
        ClaimsPrincipal   principal,
        AdviceContext     context,
        CancellationToken ct = default
    ) {

        var execution = new IdentityOperationExecution<IdentityResult<ClaimsStore>> {
            Operation = IdentityOperation.Profile,
            Principal = principal,
        };

        var handled = await RunStageAsync(
            execution,
            () => Advisor.For<IIdentityRequestAdvisor<Unit, IdentityResult<ClaimsStore>>>()
                         .RunAsync(context, Unit.Value, execution, ct));
        if (handled is not null) {
            return handled;
        }

        if (await _users.GetUserAsync(principal) is not { } found) {
            throw SchemataResourceErrors.NotFound<TUser>(principal.FindFirstValue(IdentityClaims.Subject));
        }

        var claims = new ClaimsStore();

        foreach (var claim in principal.Claims) {
            claims.AddClaim(claim.Type, claim.Value);
        }

        handled = await RunStageAsync(
            execution,
            () => Advisor.For<IIdentityProfileResponseAdvisor<TUser>>()
                         .RunAsync(context, found, claims, principal, execution, ct));
        if (handled is not null) {
            return handled;
        }

        return IdentityResult<ClaimsStore>.Success(claims);
    }

    /// <summary>Sends an email-change confirmation code for the authenticated user.</summary>
    public async Task<IdentityResult<Unit>> ChangeEmailAsync(
        ProfileRequest    request,
        ClaimsPrincipal   principal,
        AdviceContext     context,
        CancellationToken ct = default
    ) {

        var execution = new IdentityOperationExecution<IdentityResult<Unit>> {
            Operation = IdentityOperation.ChangeEmail,
            Principal = principal,
        };

        var handled = await RunStageAsync(
            execution,
            () => Advisor.For<IIdentityRequestAdvisor<ProfileRequest, IdentityResult<Unit>>>()
                         .RunAsync(context, request, execution, ct));
        if (handled is not null) {
            return handled;
        }

        if (await _users.GetUserAsync(principal) is not { } found) {
            throw SchemataResourceErrors.NotFound<TUser>(principal.FindFirstValue(IdentityClaims.Subject));
        }

        await SendConfirmationCodeAsync(found, request.EmailAddress, null);

        handled = await RunStageAsync(
            execution,
            () => Advisor.For<IIdentityProfileChangeAdvisor>()
                         .RunAsync(context, found, IdentityOperation.ChangeEmail, execution, ct));
        if (handled is not null) {
            return handled;
        }

        return IdentityResult<Unit>.Success(null);
    }

    /// <summary>Sends a phone-change confirmation code for the authenticated user.</summary>
    public async Task<IdentityResult<Unit>> ChangePhoneAsync(
        ProfileRequest    request,
        ClaimsPrincipal   principal,
        AdviceContext     context,
        CancellationToken ct = default
    ) {

        var execution = new IdentityOperationExecution<IdentityResult<Unit>> {
            Operation = IdentityOperation.ChangePhone,
            Principal = principal,
        };

        var handled = await RunStageAsync(
            execution,
            () => Advisor.For<IIdentityRequestAdvisor<ProfileRequest, IdentityResult<Unit>>>()
                         .RunAsync(context, request, execution, ct));
        if (handled is not null) {
            return handled;
        }

        if (await _users.GetUserAsync(principal) is not { } found) {
            throw SchemataResourceErrors.NotFound<TUser>(principal.FindFirstValue(IdentityClaims.Subject));
        }

        await SendConfirmationCodeAsync(found, null, request.PhoneNumber);

        handled = await RunStageAsync(
            execution,
            () => Advisor.For<IIdentityProfileChangeAdvisor>()
                         .RunAsync(context, found, IdentityOperation.ChangePhone, execution, ct));
        if (handled is not null) {
            return handled;
        }

        return IdentityResult<Unit>.Success(null);
    }

    /// <summary>Changes the authenticated user's password.</summary>
    public async Task<IdentityResult<Unit>> ChangePasswordAsync(
        ProfileRequest    request,
        ClaimsPrincipal   principal,
        AdviceContext     context,
        CancellationToken ct = default
    ) {

        var execution = new IdentityOperationExecution<IdentityResult<Unit>> {
            Operation = IdentityOperation.ChangePassword,
            Principal = principal,
        };

        var handled = await RunStageAsync(
            execution,
            () => Advisor.For<IIdentityRequestAdvisor<ProfileRequest, IdentityResult<Unit>>>()
                         .RunAsync(context, request, execution, ct));
        if (handled is not null) {
            return handled;
        }

        if (await _users.GetUserAsync(principal) is not { } found) {
            throw SchemataResourceErrors.NotFound<TUser>(principal.FindFirstValue(IdentityClaims.Subject));
        }

        var result = await _users.ChangePasswordAsync(found, request.OldPassword!, request.NewPassword!);
        if (!result.Succeeded) {
            throw new ValidationException(result.Errors.Select(e => new ErrorFieldViolation {
                Reason      = NormalizeIdentityCode(e.Code),
                Description = e.Description,
            }));
        }

        handled = await RunStageAsync(
            execution,
            () => Advisor.For<IIdentityProfileChangeAdvisor>()
                         .RunAsync(context, found, IdentityOperation.ChangePassword, execution, ct));
        if (handled is not null) {
            return handled;
        }

        return IdentityResult<Unit>.Success(null);
    }

    /// <summary>Builds two-factor authenticator enrollment state for the authenticated user.</summary>
    public async Task<IdentityResult<AuthenticatorResponse>> AuthenticatorAsync(
        ClaimsPrincipal   principal,
        AdviceContext     context,
        CancellationToken ct = default
    ) {

        var execution = new IdentityOperationExecution<IdentityResult<AuthenticatorResponse>> {
            Operation = IdentityOperation.Authenticator,
            Principal = principal,
        };

        var handled = await RunStageAsync(
            execution,
            () => Advisor.For<IIdentityRequestAdvisor<Unit, IdentityResult<AuthenticatorResponse>>>()
                         .RunAsync(context, Unit.Value, execution, ct));
        if (handled is not null) {
            return handled;
        }

        if (await _users.GetUserAsync(principal) is not { } found) {
            throw SchemataResourceErrors.NotFound<TUser>(principal.FindFirstValue(IdentityClaims.Subject));
        }

        var result = new AuthenticatorResponse { IsTwoFactorEnabled = await _users.GetTwoFactorEnabledAsync(found) };

        if (!result.IsTwoFactorEnabled) {
            RequireSucceeded(await _users.ResetAuthenticatorKeyAsync(found));
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
        AdviceContext        context,
        CancellationToken    ct = default
    ) {
        var execution = new IdentityOperationExecution<IdentityResult<Unit>> {
            Operation = IdentityOperation.Enroll,
            Principal = principal,
        };

        var handled = await RunStageAsync(
            execution,
            () => Advisor.For<IIdentityRequestAdvisor<AuthenticatorRequest, IdentityResult<Unit>>>()
                         .RunAsync(context, request, execution, ct));
        if (handled is not null) {
            return handled;
        }

        if (await _users.GetUserAsync(principal) is not { } found) {
            throw SchemataResourceErrors.NotFound<TUser>(principal.FindFirstValue(IdentityClaims.Subject));
        }

        RequireSucceeded(await _users.SetTwoFactorEnabledAsync(found, true));

        handled = await RunStageAsync(
            execution,
            () => Advisor.For<IIdentityTwoFactorAdvisor>()
                         .RunAsync(context, found, IdentityOperation.Enroll, execution, ct));
        if (handled is not null) {
            return handled;
        }

        return IdentityResult<Unit>.Success(null);
    }

    /// <summary>Disables two-factor authenticator sign-in for the authenticated user.</summary>
    public async Task<IdentityResult<Unit>> DowngradeAsync(
        AuthenticatorRequest request,
        ClaimsPrincipal      principal,
        AdviceContext        context,
        CancellationToken    ct = default
    ) {
        var execution = new IdentityOperationExecution<IdentityResult<Unit>> {
            Operation = IdentityOperation.Downgrade,
            Principal = principal,
        };

        var handled = await RunStageAsync(
            execution,
            () => Advisor.For<IIdentityRequestAdvisor<AuthenticatorRequest, IdentityResult<Unit>>>()
                         .RunAsync(context, request, execution, ct));
        if (handled is not null) {
            return handled;
        }

        if (await _users.GetUserAsync(principal) is not { } found) {
            throw SchemataResourceErrors.NotFound<TUser>(principal.FindFirstValue(IdentityClaims.Subject));
        }

        var passed = request switch {
            var _ when !string.IsNullOrWhiteSpace(request.TwoFactorCode) => await _users.VerifyTwoFactorTokenAsync(found, _users.Options.Tokens.AuthenticatorTokenProvider, request.TwoFactorCode),
            var _ when !string.IsNullOrWhiteSpace(request.TwoFactorRecoveryCode) => (await _users.RedeemTwoFactorRecoveryCodeAsync(found, request.TwoFactorRecoveryCode)).Succeeded,
            var _ => false,
        };

        if (!passed) {
            throw new InvalidArgumentException();
        }

        RequireSucceeded(await _users.SetTwoFactorEnabledAsync(found, false));
        RequireSucceeded(await _users.ResetAuthenticatorKeyAsync(found));

        handled = await RunStageAsync(
            execution,
            () => Advisor.For<IIdentityTwoFactorAdvisor>()
                         .RunAsync(context, found, IdentityOperation.Downgrade, execution, ct));
        if (handled is not null) {
            return handled;
        }

        return IdentityResult<Unit>.Success(null);
    }
}
