using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
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
    private AdviceContext? _context;
    private ClaimsPrincipal? _incoming;
    private CancellationToken _cancellation;
    private IdentityOperationExecution<IdentityResult<ClaimsPrincipal>>? _execution;
    private IdentityResult<Unit>? _result;

    /// <summary>Authenticates the supplied credentials and writes the selected sign-in response.</summary>
    public async Task<IdentityResult<Unit>> LoginAsync(
        LoginRequest      request,
        ClaimsPrincipal   principal,
        AdviceContext     context,
        CancellationToken ct = default
    ) {
        if (_request is not null) throw new InvalidOperationException("A login is already active in this scope.");
        _request = request;
        _context = context;
        _incoming = principal;
        _cancellation = ct;
        try {
            var execution = _execution = new IdentityOperationExecution<IdentityResult<ClaimsPrincipal>> {
                Operation = IdentityOperation.Login,
                Principal = principal,
            };
            var handled = await HandledResponseRunner.RunAsync(
                execution,
                () => Advisor.For<IIdentityRequestAdvisor<LoginRequest, IdentityResult<ClaimsPrincipal>>>()
                             .RunAsync(context, request, execution, ct),
                static () => new PermissionDeniedException());
            if (handled is not null) {
                await CompleteAsync(handled, new AuthenticationProperties());
                return _result!;
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
            _context = null;
            _incoming = null;
            _cancellation = default;
            _execution = null;
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
        var context = _context!;
        var execution = _execution!;

        // The login stage starts with no handled response: a request-stage write that continued
        // must not stand in for this stage's result.
        execution.ClearResponse();

        var handled = await HandledResponseRunner.RunAsync(
            execution,
            () => Advisor.For<IIdentityLoginAdvisor>().RunAsync(context, user, _request, execution, _cancellation),
            static () => new PermissionDeniedException());
        if (handled is not null) {
            await CompleteAsync(handled, authenticationProperties);
            return;
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
        if (_request is { UseCookies: true }) {
            await Context.SignInAsync(Schemes(Context).ApplicationScheme, principal, properties);
        }
        Context.User = principal;
        await Context.SignInAsync(Schemes(Context).BearerScheme, principal, properties);
        _result = IdentityResult<Unit>.Success(Unit.Value);
    }

    // The executing request scope decides the profile: a host scope reads the global Identity
    // constants, a tenant scope reads the tenant options naming its UseCookie/bearer schemes.
    private static SchemataIdentityOptions Schemes(HttpContext http) {
        return http.RequestServices.GetRequiredService<IOptions<SchemataIdentityOptions>>().Value;
    }
}
