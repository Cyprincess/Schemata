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
            new("amr", """["pwd","otp","mfa"]""", "JSON"),
            new("acr", AuthenticationContextClasses.Multifactor),
            new("auth_time", "1767225600", ClaimValueTypes.Integer64),
        ], "ticket"));
        var ticket = new AuthenticationTicket(original, new AuthenticationProperties {
            ExpiresUtc = Anchor.AddMinutes(5),
        }, "ticket");
        host.SignIn.Setup(value => value.ValidateSecurityStampAsync(original)).ReturnsAsync(User);
        StubPrincipalFactory(host);

        var result = await host.Handler.RefreshAsync(ticket, Principal);

        AssertPrincipalStamped(result, AuthenticationContextClasses.Multifactor, """["pwd","otp","mfa"]""", 1767225600);
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

        var principal  = result.Data!;
        var stampedAmr = principal.FindFirst("amr");
        Assert.Equal(amr, stampedAmr?.Value);
        // The authorization pipeline decodes the array only from a JSON-typed claim.
        Assert.Equal("JSON", stampedAmr?.ValueType);
        var stampedAuthTime = principal.FindFirst("auth_time");
        Assert.Equal((authTime ?? Anchor.ToUnixTimeSeconds()).ToString(), stampedAuthTime?.Value);
        Assert.Equal(ClaimValueTypes.Integer64, stampedAuthTime?.ValueType);
        Assert.Equal(acr, principal.FindFirst("acr")?.Value);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() { return now; }
    }
}
