using Schemata.Identity.Tests.Fixtures;
using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Exceptions;
using Schemata.Identity.Skeleton;
using Schemata.Identity.Skeleton.Entities;
using Xunit;
using AspNetIdentityResult = Microsoft.AspNetCore.Identity.IdentityResult;
using IdentitySignInResult = Microsoft.AspNetCore.Identity.SignInResult;

namespace Schemata.Identity.Tests;

public class AuthenticationContextClaimsShould
{
    private static readonly DateTimeOffset Anchor = new(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);

    private static readonly SchemataUser User = new() { UserName = "alice" };

    private static readonly ClaimsPrincipal Principal = new(new ClaimsIdentity("test"));

    [Fact]
    public async Task Login_With_Password_Alone_Stamps_The_Password_Context() {
        using var host = Host();
        host.Users.Setup(value => value.FindByNameAsync("alice")).ReturnsAsync(User);
        host.SignIn.Setup(value => value.CheckPasswordSignInAsync(User, "password", true))
            .ReturnsAsync(IdentitySignInResult.Success);
        StubPrincipalFactory(host);

        var result = await host.Handler.LoginAsync(new() { Username = "alice", Password = "password" }, Principal);

        AssertPrincipalStamped(result, AuthenticationContextClasses.Password, """["pwd"]""");
    }

    [Fact]
    public async Task Login_With_Verified_Authenticator_Stamps_The_Multifactor_Context() {
        using var host = Host();
        host.Users.Setup(value => value.FindByNameAsync("alice")).ReturnsAsync(User);
        host.SignIn.Setup(value => value.CheckPasswordSignInAsync(User, "password", true))
            .ReturnsAsync(IdentitySignInResult.TwoFactorRequired);
        host.Users.Setup(value => value.VerifyTwoFactorTokenAsync(
                              User, host.SignIn.Object.Options.Tokens.AuthenticatorTokenProvider, "123456"))
            .ReturnsAsync(true);
        StubPrincipalFactory(host);

        var result = await host.Handler.LoginAsync(
            new() { Username = "alice", Password = "password", TwoFactorCode = "123456" }, Principal);

        AssertPrincipalStamped(result, AuthenticationContextClasses.Multifactor, """["pwd","otp","mfa"]""");
    }

    [Fact]
    public async Task Login_With_Recovery_Code_Stamps_The_Multifactor_Context() {
        using var host = Host();
        host.Users.Setup(value => value.FindByNameAsync("alice")).ReturnsAsync(User);
        host.SignIn.Setup(value => value.CheckPasswordSignInAsync(User, "password", true))
            .ReturnsAsync(IdentitySignInResult.TwoFactorRequired);
        host.Users.Setup(value => value.RedeemTwoFactorRecoveryCodeAsync(User, "recovery-code"))
            .ReturnsAsync(AspNetIdentityResult.Success);
        StubPrincipalFactory(host);

        var result = await host.Handler.LoginAsync(
            new() { Username = "alice", Password = "password", TwoFactorRecoveryCode = "recovery-code" }, Principal);

        AssertPrincipalStamped(result, AuthenticationContextClasses.Multifactor, """["pwd","otp","mfa"]""");
    }

    [Fact]
    public async Task Register_Stamps_The_Password_Context_On_The_Sign_In_Principal() {
        using var host = Host();
        host.Users.Setup(value => value.CreateAsync(It.IsAny<SchemataUser>(), "password"))
            .ReturnsAsync(AspNetIdentityResult.Success);
        StubPrincipalFactory(host);

        var result = await host.Handler.RegisterAsync(
            new() { Username = "alice", Password = "password" }, Principal);

        AssertPrincipalStamped(result, AuthenticationContextClasses.Password, """["pwd"]""");
    }

    [Fact]
    public async Task Refresh_Carries_The_Original_Context_Onto_The_Rebuilt_Principal() {
        using var host = Host();
        var original = new ClaimsPrincipal(new ClaimsIdentity([
            new("amr", """["pwd","otp","mfa"]"""),
            new("acr", AuthenticationContextClasses.Multifactor),
            new("auth_time", "1767225600"),
        ], "ticket"));
        var ticket = new AuthenticationTicket(original, "ticket");
        host.SignIn.Setup(value => value.ValidateSecurityStampAsync(original)).ReturnsAsync(User);
        StubPrincipalFactory(host);

        var result = await host.Handler.RefreshAsync(ticket, Principal);

        AssertPrincipalStamped(result, AuthenticationContextClasses.Multifactor, """["pwd","otp","mfa"]""", 1767225600);
    }

    [Fact]
    public async Task Login_With_A_Failed_Password_Yields_No_Principal() {
        using var host = Host();
        host.Users.Setup(value => value.FindByNameAsync("alice")).ReturnsAsync(User);
        host.SignIn.Setup(value => value.CheckPasswordSignInAsync(User, "password", true))
            .ReturnsAsync(IdentitySignInResult.Failed);

        await Assert.ThrowsAsync<UnauthenticatedException>(
            () => host.Handler.LoginAsync(new() { Username = "alice", Password = "password" }, Principal));
    }

    private static IdentityHandlerTestHost Host() {
        return new(services => services.AddSingleton<TimeProvider>(new FixedClock(Anchor)));
    }

    private static void StubPrincipalFactory(IdentityHandlerTestHost host) {
        host.SignIn.Setup(value => value.CreateUserPrincipalAsync(It.IsAny<SchemataUser>()))
            .ReturnsAsync(new ClaimsPrincipal(new ClaimsIdentity("login")));
    }

    private static void AssertPrincipalStamped(
        IdentityResult<ClaimsPrincipal> result,
        string                          acr,
        string                          amr,
        long?                           authTime = null
    ) {
        Assert.Equal(IdentityStatus.Success, result.Status);

        var principal = result.Data!;
        Assert.Equal(amr, principal.FindFirst("amr")?.Value);
        Assert.Equal(acr, principal.FindFirst("acr")?.Value);
        Assert.Equal(
            (authTime ?? Anchor.ToUnixTimeSeconds()).ToString(),
            principal.FindFirst("auth_time")?.Value);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() { return now; }
    }
}
