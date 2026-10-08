using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Advice;
using Schemata.Identity.Skeleton;
using Schemata.Identity.Skeleton.Advisors;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Models;

namespace Schemata.Identity.Foundation;

/// <summary>Runs platform credential verification before Schemata login policy and credential issuance.</summary>
/// <typeparam name="TUser">The identity user type.</typeparam>
public class SchemataSignInManager<TUser>(
    UserManager<TUser> users,
    IHttpContextAccessor accessor,
    IUserClaimsPrincipalFactory<TUser> claims,
    IOptions<IdentityOptions> options,
    ILogger<SignInManager<TUser>> logger,
    IAuthenticationSchemeProvider schemes,
    IUserConfirmation<TUser> confirmation,
    IEnumerable<IHostSignInObserver> observers,
    TimeProvider? time = null
) : SignInManager<TUser>(users, accessor, claims, options, logger, schemes, confirmation)
    where TUser : SchemataUser, new()
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private LoginRequest? _request;
    private ClaimsPrincipal? _incoming;
    private CancellationToken _cancellation;
    private IdentityResult<Unit>? _result;

    /// <summary>Authenticates the supplied credentials and writes the selected sign-in response.</summary>
    public async Task<IdentityResult<Unit>> LoginAsync(LoginRequest request, ClaimsPrincipal principal, CancellationToken ct = default) {
        if (_request is not null) throw new InvalidOperationException("A login is already active in this scope.");
        _request = request;
        _incoming = principal;
        _cancellation = ct;
        try {
            var context = AdviceContext.Require();
            switch (await Advisor.For<IIdentityRequestAdvisor<LoginRequest>>()
                                 .RunAsync(context, request, IdentityOperation.Login, principal, ct)) {
                case AdviseResult.Continue:
                    break;
                case AdviseResult.Handle when context.TryGet<IdentityResult<ClaimsPrincipal>>(out var response):
                    await CompleteAsync(response!, new AuthenticationProperties());
                    return _result!;
                default:
                    throw new PermissionDeniedException();
            }

            var result = await PasswordSignInAsync(request.Username, request.Password, false, true);
            if (result.RequiresTwoFactor) {
                if (!string.IsNullOrWhiteSpace(request.TwoFactorCode)) {
                    result = await TwoFactorAuthenticatorSignInAsync(request.TwoFactorCode, false, false);
                } else if (!string.IsNullOrWhiteSpace(request.TwoFactorRecoveryCode)) {
                    result = await TwoFactorRecoveryCodeSignInAsync(request.TwoFactorRecoveryCode);
                }
            }
            if (result.RequiresTwoFactor) return IdentityResult<Unit>.Challenge();
            if (!result.Succeeded) throw new UnauthenticatedException();
            return _result ?? throw new InvalidOperationException("Login completed without an issuance result.");
        } finally {
            _request = null;
            _incoming = null;
            _cancellation = default;
            _result = null;
        }
    }

    /// <inheritdoc />
    public override async Task SignInWithClaimsAsync(TUser user, AuthenticationProperties? authenticationProperties,
                                                    IEnumerable<Claim> additionalClaims) {
        if (_request is null) {
            await base.SignInWithClaimsAsync(user, authenticationProperties, additionalClaims);
            return;
        }
        var context = AdviceContext.Require();
        switch (await Advisor.For<IIdentityLoginAdvisor>().RunAsync(context, user, _request, _cancellation)) {
            case AdviseResult.Continue:
                break;
            case AdviseResult.Handle when context.TryGet<IdentityResult<ClaimsPrincipal>>(out var response):
                await CompleteAsync(response!, authenticationProperties);
                return;
            default:
                throw new PermissionDeniedException();
        }

        var principal = await CreateUserPrincipalAsync(user);
        var identity = (ClaimsIdentity)principal.Identity!;
        var multifactor = false;
        foreach (var claim in additionalClaims) {
            if (claim.Type == "amr") {
                multifactor |= claim.Value == "mfa";
            } else {
                identity.AddClaim(claim);
            }
        }
        AuthenticationClaims.Stamp(principal, _request.AcrValues, multifactor, _time);
        await CompleteAsync(IdentityResult<ClaimsPrincipal>.Success(principal), authenticationProperties);
    }

    private async Task CompleteAsync(IdentityResult<ClaimsPrincipal> result, AuthenticationProperties? properties) {
        if (result.Status == IdentityStatus.Challenge) {
            _result = IdentityResult<Unit>.Challenge();
            return;
        }
        var principal = result.Data ?? throw new InvalidOperationException("Successful login requires a principal.");
        foreach (var observer in observers) {
            await observer.OnSigningInAsync(principal, _incoming!, _cancellation);
        }
        if (_request!.UseCookies == true) {
            await Context.SignInAsync(IdentityConstants.ApplicationScheme, principal, properties);
        }
        Context.User = principal;
        await Context.SignInAsync(IdentityConstants.BearerScheme, principal, properties);
        _result = IdentityResult<Unit>.Success(Unit.Value);
    }
}
