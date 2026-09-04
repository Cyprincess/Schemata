using Schemata.Identity.Tests.Fixtures;
using System.Security.Claims;
using System.Threading.Tasks;
using Moq;
using Schemata.Identity.Skeleton;
using Schemata.Identity.Skeleton.Entities;
using Xunit;
using IdentitySignInResult = Microsoft.AspNetCore.Identity.SignInResult;

namespace Schemata.Identity.Tests;

public class RequestedAcrValuesShould
{
    private static readonly SchemataUser User = new() { UserName = "alice" };

    private static readonly ClaimsPrincipal Principal = new(new ClaimsIdentity("test"));

    [Fact]
    public async Task Login_Stronger_Performed_Authentication_Covers_A_Weaker_Requested_Level() {
        using var host = Host();
        Setup(host, multifactor: true);

        var result = await host.Handler.LoginAsync(
            new() { Username = "alice", Password = "password", TwoFactorCode = "123456", AcrValues = AuthenticationContextClasses.Password },
            Principal);

        Assert.Equal(AuthenticationContextClasses.Password, result.Data!.FindFirst("acr")?.Value);
    }

    [Fact]
    public async Task Login_Weaker_Performed_Authentication_Cannot_Satisfy_A_Stronger_Request() {
        using var host = Host();
        Setup(host, multifactor: false);

        var result = await host.Handler.LoginAsync(
            new() { Username = "alice", Password = "password", AcrValues = AuthenticationContextClasses.Multifactor },
            Principal);

        // Core §5.5.1.1: the unsatisfied voluntary request keeps the performed level.
        Assert.Equal(AuthenticationContextClasses.Password, result.Data!.FindFirst("acr")?.Value);
    }

    [Fact]
    public async Task Login_Prefers_The_First_Satisfied_Value_In_Request_Order() {
        using var host = Host();
        Setup(host, multifactor: true);

        var result = await host.Handler.LoginAsync(
            new() {
                Username       = "alice",
                Password       = "password",
                TwoFactorCode  = "123456",
                AcrValues      = $"{AuthenticationContextClasses.Multifactor} {AuthenticationContextClasses.Password}",
            },
            Principal);

        Assert.Equal(AuthenticationContextClasses.Multifactor, result.Data!.FindFirst("acr")?.Value);
    }

    [Fact]
    public async Task Login_Request_Of_Unknown_Classes_Keeps_The_Performed_Class() {
        using var host = Host();
        Setup(host, multifactor: false);

        var result = await host.Handler.LoginAsync(
            new() { Username = "alice", Password = "password", AcrValues = "urn:example:acr:vip" },
            Principal);

        Assert.Equal(AuthenticationContextClasses.Password, result.Data!.FindFirst("acr")?.Value);
    }

    [Fact]
    public async Task Login_Without_Requested_Values_Stamps_The_Performed_Class() {
        using var host = Host();
        Setup(host, multifactor: true);

        var result = await host.Handler.LoginAsync(
            new() { Username = "alice", Password = "password", TwoFactorCode = "123456", AcrValues = "   " },
            Principal);

        Assert.Equal(AuthenticationContextClasses.Multifactor, result.Data!.FindFirst("acr")?.Value);
    }

    private static IdentityHandlerTestHost Host() {
        return new(_ => { });
    }

    private static void Setup(IdentityHandlerTestHost host, bool multifactor) {
        host.Users.Setup(value => value.FindByNameAsync("alice")).ReturnsAsync(User);
        if (multifactor) {
            host.SignIn.Setup(value => value.CheckPasswordSignInAsync(User, "password", true))
                .ReturnsAsync(IdentitySignInResult.TwoFactorRequired);
            host.Users.Setup(value => value.VerifyTwoFactorTokenAsync(
                                  User, host.SignIn.Object.Options.Tokens.AuthenticatorTokenProvider, "123456"))
                .ReturnsAsync(true);
        } else {
            host.SignIn.Setup(value => value.CheckPasswordSignInAsync(User, "password", true))
                .ReturnsAsync(IdentitySignInResult.Success);
        }

        host.SignIn.Setup(value => value.CreateUserPrincipalAsync(It.IsAny<SchemataUser>()))
            .ReturnsAsync(new ClaimsPrincipal(new ClaimsIdentity("login")));
    }
}
