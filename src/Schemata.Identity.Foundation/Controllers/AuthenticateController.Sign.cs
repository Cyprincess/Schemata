using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Advisors;
using Schemata.Advice;
using Schemata.Identity.Foundation.Advisors;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Models;

namespace Schemata.Identity.Foundation.Controllers;

public sealed partial class AuthenticateController : ControllerBase
{
    [HttpPost(nameof(Register))]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request) {
        if (!_options.CurrentValue.AllowRegistration) {
            return NotFound();
        }

        var ctx = new AdviceContext(_sp);

        switch (await Advisor.For<IIdentityRegisterRequestAdvisor>()
                             .RunAsync(ctx, request, HttpContext, HttpContext.RequestAborted)) {
            case AdviseResult.Block:
            case AdviseResult.Handle:
                return EmptyResult;
            case AdviseResult.Continue:
                break;
        }

        var username = request.EmailAddress ?? request.PhoneNumber;

        var user = new SchemataUser {
            UserName = username, Email = request.EmailAddress, PhoneNumber = request.PhoneNumber,
        };

        switch (await Advisor.For<IIdentityRegisterUserAdvisor>()
                             .RunAsync(ctx, user, HttpContext, HttpContext.RequestAborted)) {
            case AdviseResult.Block:
            case AdviseResult.Handle:
                return EmptyResult;
            case AdviseResult.Continue:
                break;
        }

        var result = await _userManager.CreateAsync(user, request.Password);

        if (!result.Succeeded) {
            return BadRequest(result.Errors);
        }

        if (_userManager.Options.SignIn.RequireConfirmedAccount) {
            await SendConfirmationCodeAsync(user, request.EmailAddress, request.PhoneNumber);
        }

        switch (await Advisor.For<IIdentityRegisterAdvisor>()
                             .RunAsync(ctx, user, HttpContext, HttpContext.RequestAborted)) {
            case AdviseResult.Block:
            case AdviseResult.Handle:
                return EmptyResult;
            case AdviseResult.Continue:
                break;
        }

        return NoContent();
    }

    [HttpPost(nameof(Login))]
    public async Task<IActionResult> Login([FromBody] LoginRequest request) {
        var signInManager = _sp.GetRequiredService<SignInManager<SchemataUser>>();

        signInManager.AuthenticationScheme = IdentityConstants.BearerScheme;

        var result = await signInManager.PasswordSignInAsync(request.Username, request.Password, false, true);

        if (result.RequiresTwoFactor) {
            if (!string.IsNullOrWhiteSpace(request.TwoFactorCode)) {
                result = await signInManager.TwoFactorAuthenticatorSignInAsync(request.TwoFactorCode, false, false);
            } else if (!string.IsNullOrWhiteSpace(request.TwoFactorRecoveryCode)) {
                result = await signInManager.TwoFactorRecoveryCodeSignInAsync(request.TwoFactorRecoveryCode);
            }
        }

        if (!result.Succeeded) {
            return BadRequest(result.ToString());
        }

        return EmptyResult;
    }

    [HttpPost(nameof(Refresh))]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request) {
        var signInManager = _sp.GetRequiredService<SignInManager<SchemataUser>>();

        var protector = _bearerToken.Get(IdentityConstants.ApplicationScheme).RefreshTokenProtector;
        var ticket    = protector.Unprotect(request.RefreshToken);

        if (ticket?.Properties?.ExpiresUtc is not { } expires
         || DateTimeOffset.UtcNow >= expires
         || await signInManager.ValidateSecurityStampAsync(ticket.Principal) is not { } user) {
            return Challenge();
        }

        var principal = await signInManager.CreateUserPrincipalAsync(user);

        return SignIn(principal, IdentityConstants.ApplicationScheme);
    }
}
