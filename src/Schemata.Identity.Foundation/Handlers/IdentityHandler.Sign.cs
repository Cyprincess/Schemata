using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
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
    /// <summary>
    ///     Normalizes an ASP.NET Identity error code (PascalCase, e.g.
    ///     <c>"PasswordTooShort"</c>) into the AIP-193 UPPER_SNAKE_CASE form
    ///     (<c>"PASSWORD_TOO_SHORT"</c>) required by <see cref="ErrorFieldViolation.Reason" />.
    /// </summary>
    private static string NormalizeIdentityCode(string? code) {
        if (string.IsNullOrEmpty(code)) {
            return "IDENTITY_VALIDATION_FAILED";
        }

        var builder = new StringBuilder(code.Length + 8);
        for (var i = 0; i < code.Length; i++) {
            var c = code[i];
            if (i > 0 && char.IsUpper(c) && (char.IsLower(code[i - 1]) || char.IsDigit(code[i - 1]))) {
                builder.Append('_');
            }

            builder.Append(char.ToUpperInvariant(c));
        }

        return builder.ToString();
    }

    /// <summary>Registers a user and builds the sign-in principal.</summary>
    public async Task<IdentityResult<ClaimsPrincipal>> RegisterAsync(
        RegisterRequest   request,
        ClaimsPrincipal   principal,
        CancellationToken ct = default
    ) {
        var ctx = new AdviceContext(_sp);

        switch (await Advisor.For<IIdentityRequestAdvisor<RegisterRequest>>()
                             .RunAsync(ctx, request, IdentityOperation.Register, principal, ct)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<IdentityResult<ClaimsPrincipal>>(out var response):
                return response!;
            case AdviseResult.Block:
            default:
                throw new PermissionDeniedException();
        }

        var user = new TUser {
            UserName = request.Username,
            Email = request.EmailAddress,
            PhoneNumber = request.PhoneNumber,
        };

        switch (await Advisor.For<IIdentityRegisterUserAdvisor>()
                             .RunAsync(ctx, user, request, ct)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<IdentityResult<ClaimsPrincipal>>(out var response):
                return response!;
            case AdviseResult.Block:
            default:
                throw new PermissionDeniedException();
        }

        var result = await _users.CreateAsync(user, request.Password);
        if (!result.Succeeded) {
            throw new ValidationException(result.Errors.Select(e => new ErrorFieldViolation {
                Reason      = NormalizeIdentityCode(e.Code),
                Description = e.Description,
            }));
        }

        switch (await Advisor.For<IIdentityRegisterAdvisor<TUser>>()
                             .RunAsync(ctx, user, request, ct)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<IdentityResult<ClaimsPrincipal>>(out var response):
                return response!;
            case AdviseResult.Block:
            default:
                throw new PermissionDeniedException();
        }

        if (_users.Options.SignIn.RequireConfirmedAccount) {
            await SendConfirmationCodeAsync(user, request.EmailAddress, request.PhoneNumber);
        }

        var claims = await _sign.CreateUserPrincipalAsync(user);

        return IdentityResult<ClaimsPrincipal>.Success(claims);
    }

    /// <summary>Authenticates a user and builds the sign-in principal.</summary>
    public async Task<IdentityResult<ClaimsPrincipal>> LoginAsync(
        LoginRequest      request,
        ClaimsPrincipal   principal,
        CancellationToken ct = default
    ) {
        var ctx = new AdviceContext(_sp);

        switch (await Advisor.For<IIdentityRequestAdvisor<LoginRequest>>()
                             .RunAsync(ctx, request, IdentityOperation.Login, principal, ct)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<IdentityResult<ClaimsPrincipal>>(out var response):
                return response!;
            case AdviseResult.Block:
            default:
                throw new PermissionDeniedException();
        }

        var found = await _users.FindByNameAsync(request.Username);
        if (found is null) {
            throw new UnauthenticatedException();
        }

        var check = await _sign.CheckPasswordSignInAsync(found, request.Password, true);

        if (check.RequiresTwoFactor) {
            if (!string.IsNullOrWhiteSpace(request.TwoFactorCode)) {
                var valid = await _users.VerifyTwoFactorTokenAsync(
                    found, _sign.Options.Tokens.AuthenticatorTokenProvider, request.TwoFactorCode);
                if (!valid) {
                    throw new UnauthenticatedException();
                }
            } else if (!string.IsNullOrWhiteSpace(request.TwoFactorRecoveryCode)) {
                var redeem = await _users.RedeemTwoFactorRecoveryCodeAsync(found, request.TwoFactorRecoveryCode);
                if (!redeem.Succeeded) {
                    throw new UnauthenticatedException();
                }
            } else {
                return IdentityResult<ClaimsPrincipal>.Challenge();
            }

            await _users.ResetAccessFailedCountAsync(found);
        } else if (!check.Succeeded) {
            throw new UnauthenticatedException();
        }

        switch (await Advisor.For<IIdentityLoginAdvisor>()
                             .RunAsync(ctx, found, request, ct)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<IdentityResult<ClaimsPrincipal>>(out var response):
                return response!;
            case AdviseResult.Block:
            default:
                throw new PermissionDeniedException();
        }

        var claims = await _sign.CreateUserPrincipalAsync(found);

        return IdentityResult<ClaimsPrincipal>.Success(claims);
    }

    /// <summary>Refreshes a sign-in principal from an authentication ticket.</summary>
    public async Task<IdentityResult<ClaimsPrincipal>> RefreshAsync(
        AuthenticationTicket? ticket,
        ClaimsPrincipal       principal,
        CancellationToken     ct = default
    ) {
        var ctx = new AdviceContext(_sp);

        switch (await Advisor.For<IIdentityRequestAdvisor<Unit>>()
                             .RunAsync(ctx, Unit.Value, IdentityOperation.Refresh, principal, ct)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<IdentityResult<ClaimsPrincipal>>(out var response):
                return response!;
            case AdviseResult.Block:
            default:
                throw new PermissionDeniedException();
        }

        if (ticket?.Principal is null || await _sign.ValidateSecurityStampAsync(ticket.Principal) is not { } found) {
            return IdentityResult<ClaimsPrincipal>.Challenge();
        }

        switch (await Advisor.For<IIdentityRefreshUserAdvisor<TUser>>()
                             .RunAsync(ctx, found, ct)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<IdentityResult<ClaimsPrincipal>>(out var response):
                return response!;
            case AdviseResult.Block:
            default:
                throw new PermissionDeniedException();
        }

        var claims = await _sign.CreateUserPrincipalAsync(found);

        switch (await Advisor.For<IIdentityRefreshAdvisor>()
                             .RunAsync(ctx, claims, ct)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when ctx.TryGet<IdentityResult<ClaimsPrincipal>>(out var response):
                return response!;
            case AdviseResult.Block:
            default:
                throw new PermissionDeniedException();
        }

        return IdentityResult<ClaimsPrincipal>.Success(claims);
    }
}
