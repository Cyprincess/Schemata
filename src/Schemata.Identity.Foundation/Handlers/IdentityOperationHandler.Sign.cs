using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
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

internal sealed partial class IdentityOperationHandler<TUser>
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
        var ctx = AdviceContext.Require();

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
        StampAuthenticationContext(claims, AuthenticationContextClasses.Password, PasswordMethods);

        return IdentityResult<ClaimsPrincipal>.Success(claims);
    }

    /// <summary>Authenticates a user and builds the sign-in principal.</summary>
    public async Task<IdentityResult<ClaimsPrincipal>> LoginAsync(
        LoginRequest      request,
        ClaimsPrincipal   principal,
        CancellationToken ct = default
    ) {
        var ctx = AdviceContext.Require();

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

        var multifactor = false;
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
            multifactor = true;
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
        StampAuthenticationContext(
            claims,
            ResolveAuthenticationContextClass(
                request.AcrValues,
                multifactor ? AuthenticationContextClasses.Multifactor : AuthenticationContextClasses.Password),
            multifactor ? MultifactorMethods : PasswordMethods);

        return IdentityResult<ClaimsPrincipal>.Success(claims);
    }

    /// <summary>Refreshes a sign-in principal from an authentication ticket.</summary>
    public async Task<IdentityResult<ClaimsPrincipal>> RefreshAsync(
        AuthenticationTicket? ticket,
        ClaimsPrincipal       principal,
        CancellationToken     ct = default
    ) {
        var ctx = AdviceContext.Require();

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
        CarryAuthenticationContext(ticket.Principal, claims);

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

    /// <summary>OIDC claim names the login pipeline stamps; kept as literals so Schemata.Identity stays independent of Schemata.Authorization.</summary>
    private const string AmrClaim      = "amr";
    private const string AcrClaim      = "acr";
    private const string AuthTimeClaim = "auth_time";

    /// <summary>
    ///     RFC 8176 §2 method references: <c>pwd</c> for the password factor, <c>otp</c> for the
    ///     single-use second factor (a verified authenticator code or a redeemed recovery code is
    ///     a one-time password), and <c>mfa</c> because more than one factor was used — §2
    ///     directs that specific methods accompany <c>mfa</c>.
    /// </summary>
    private static readonly string[] PasswordMethods    = ["pwd"];
    private static readonly string[] MultifactorMethods = ["pwd", "otp", "mfa"];

    /// <summary>
    ///     Asserts the just-verified authentication event on the sign-in principal as OIDC
    ///     claims: <c>amr</c> (JSON array of RFC 8176 method references), <c>acr</c> (the
    ///     satisfied <see cref="AuthenticationContextClasses" /> member), and <c>auth_time</c>
    ///     (Unix seconds). OpenID Connect Core 1.0 §2 and RFC 9068 §2.2.1. The claims are
    ///     evidence for the authorization pipeline's claim read; token-wire tagging happens at
    ///     claim assembly.
    /// </summary>
    private void StampAuthenticationContext(ClaimsPrincipal principal, string acr, IReadOnlyList<string> amr) {
        var identity = (ClaimsIdentity)principal.Identity!;
        identity.AddClaim(new(AuthTimeClaim, _time.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)));
        identity.AddClaim(new(AcrClaim, acr));
        identity.AddClaim(new(AmrClaim, JsonSerializer.Serialize(amr)));
    }

    /// <summary>
    ///     Names the class the authentication satisfied among the requested
    ///     <c>acr_values</c> (OpenID Connect Core 1.0 §3.1.2.1): the first requested value in
    ///     preference order the performed class reaches in
    ///     <see cref="AuthenticationContextClasses.Supported" /> order — a stronger performed
    ///     authentication covers a weaker requested level. A request the authentication cannot
    ///     satisfy keeps <paramref name="achieved" />, the voluntary-claim outcome Core §5.5.1.1
    ///     prescribes; only the essential <c>claims</c>-parameter form (which this server does
    ///     not implement) can turn an unsatisfied request into a failure.
    /// </summary>
    private static string ResolveAuthenticationContextClass(string? requested, string achieved) {
        if (string.IsNullOrWhiteSpace(requested)) {
            return achieved;
        }

        var classes = AuthenticationContextClasses.Supported;
        var reached = Rank(classes, achieved);
        if (reached < 0) {
            return achieved;
        }

        foreach (var value in requested.Split(' ', StringSplitOptions.RemoveEmptyEntries)) {
            if (Rank(classes, value) >= reached) {
                return value;
            }
        }

        return achieved;
    }

    private static int Rank(IReadOnlyList<string> classes, string value) {
        for (var i = 0; i < classes.Count; i++) {
            if (classes[i] == value) {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    ///     Carries the authentication-event claims of the original sign-in onto a rebuilt
    ///     principal: a refresh continues the same authentication, so Core §2 <c>auth_time</c>
    ///     and the RFC 8176 method set must not reset.
    /// </summary>
    private static void CarryAuthenticationContext(ClaimsPrincipal source, ClaimsPrincipal target) {
        var identity = (ClaimsIdentity)target.Identity!;
        foreach (var type in new[] { AmrClaim, AcrClaim, AuthTimeClaim }) {
            foreach (var claim in source.FindAll(type)) {
                identity.AddClaim(new(claim.Type, claim.Value, claim.ValueType));
            }
        }
    }
}
