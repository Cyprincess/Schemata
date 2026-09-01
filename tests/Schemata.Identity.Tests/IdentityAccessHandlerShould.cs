using Schemata.Identity.Tests.Fixtures;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Moq;
using Schemata.Identity.Skeleton;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Models;
using Xunit;
using AspNetIdentityResult = Microsoft.AspNetCore.Identity.IdentityResult;

namespace Schemata.Identity.Tests;

public class IdentityAccessHandlerShould
{
    private static readonly SchemataUser User = new() { UserName = "alice" };

    private static readonly ClaimsPrincipal Principal = new(
        new ClaimsIdentity([new(ClaimTypes.NameIdentifier, "alice")], "test"));

    [Fact]
    public async Task Register_Creates_User_And_Returns_Principal() {
        using var host = new IdentityHandlerTestHost();
        host.Users.Setup(value => value.CreateAsync(It.IsAny<SchemataUser>(), "secret"))
                  .ReturnsAsync(AspNetIdentityResult.Success);
        host.SignIn.Setup(value => value.CreateUserPrincipalAsync(It.IsAny<SchemataUser>()))
                   .ReturnsAsync(Principal);
        var request = new RegisterRequest {
            Username     = "alice",
            EmailAddress = "alice@example.com",
            PhoneNumber  = "+12025550123",
            Password     = "secret",
        };

        var result = await host.Handler.RegisterAsync(request, new ClaimsPrincipal(), CancellationToken.None);

        Assert.Equal(IdentityStatus.Success, result.Status);
        Assert.Same(Principal, result.Data);
        host.Users.Verify(value => value.CreateAsync(
                              It.Is<SchemataUser>(user => user.UserName == request.Username
                                                       && user.Email == request.EmailAddress
                                                       && user.PhoneNumber == request.PhoneNumber),
                              request.Password), Times.Once);
    }

    [Fact]
    public async Task Login_With_Valid_Password_Returns_Principal() {
        using var host = new IdentityHandlerTestHost();
        host.Users.Setup(value => value.FindByNameAsync("alice")).ReturnsAsync(User);
        host.SignIn.Setup(value => value.CheckPasswordSignInAsync(User, "secret", true))
                   .ReturnsAsync(SignInResult.Success);
        host.SignIn.Setup(value => value.CreateUserPrincipalAsync(User)).ReturnsAsync(Principal);

        var result = await host.Handler.LoginAsync(
            new() { Username = "alice", Password = "secret" },
            new ClaimsPrincipal(),
            CancellationToken.None);

        Assert.Equal(IdentityStatus.Success, result.Status);
        Assert.Same(Principal, result.Data);
    }

    [Fact]
    public async Task Login_Requiring_TwoFactor_Without_Code_Returns_Challenge() {
        using var host = new IdentityHandlerTestHost();
        host.Users.Setup(value => value.FindByNameAsync("alice")).ReturnsAsync(User);
        host.SignIn.Setup(value => value.CheckPasswordSignInAsync(User, "secret", true))
                   .ReturnsAsync(SignInResult.TwoFactorRequired);

        var result = await host.Handler.LoginAsync(
            new() { Username = "alice", Password = "secret" },
            new ClaimsPrincipal(),
            CancellationToken.None);

        Assert.Equal(IdentityStatus.Challenge, result.Status);
        Assert.Null(result.Data);
        host.SignIn.Verify(value => value.CreateUserPrincipalAsync(It.IsAny<SchemataUser>()), Times.Never);
    }

    [Fact]
    public async Task Refresh_With_Valid_Ticket_Returns_Refreshed_Principal() {
        using var host = new IdentityHandlerTestHost();
        var ticketPrincipal = new ClaimsPrincipal(new ClaimsIdentity("refresh"));
        var ticket = new AuthenticationTicket(ticketPrincipal, "refresh");
        host.SignIn.Setup(value => value.ValidateSecurityStampAsync(ticketPrincipal)).ReturnsAsync(User);
        host.SignIn.Setup(value => value.CreateUserPrincipalAsync(User)).ReturnsAsync(Principal);

        var result = await host.Handler.RefreshAsync(ticket, new ClaimsPrincipal(), CancellationToken.None);

        Assert.Equal(IdentityStatus.Success, result.Status);
        Assert.Same(Principal, result.Data);
    }

    [Fact]
    public async Task Refresh_Without_Ticket_Returns_Challenge() {
        using var host = new IdentityHandlerTestHost();

        var result = await host.Handler.RefreshAsync(null, new ClaimsPrincipal(), CancellationToken.None);

        Assert.Equal(IdentityStatus.Challenge, result.Status);
        Assert.Null(result.Data);
    }

    [Fact]
    public async Task Profile_Returns_Caller_Claims() {
        using var host = new IdentityHandlerTestHost();
        host.Users.Setup(value => value.GetUserAsync(Principal)).ReturnsAsync(User);

        var result = await host.Handler.ProfileAsync(Principal, CancellationToken.None);

        Assert.Equal(IdentityStatus.Success, result.Status);
        Assert.NotNull(result.Data);
        Assert.Contains("alice", result.Data[ClaimTypes.NameIdentifier]);
    }

    [Fact]
    public async Task Downgrade_With_Valid_Code_Disables_TwoFactor_And_Resets_Key() {
        using var host = new IdentityHandlerTestHost();
        host.Users.Setup(value => value.GetUserAsync(Principal)).ReturnsAsync(User);
        host.Users.Setup(value => value.VerifyTwoFactorTokenAsync(
                             User, It.IsAny<string>(), "123456"))
                  .ReturnsAsync(true);
        host.Users.Setup(value => value.SetTwoFactorEnabledAsync(User, false))
                  .ReturnsAsync(AspNetIdentityResult.Success);
        host.Users.Setup(value => value.ResetAuthenticatorKeyAsync(User))
                  .ReturnsAsync(AspNetIdentityResult.Success);

        var result = await host.Handler.DowngradeAsync(
            new() { TwoFactorCode = "123456" }, Principal, CancellationToken.None);

        Assert.Equal(IdentityStatus.Success, result.Status);
        host.Users.Verify(value => value.SetTwoFactorEnabledAsync(User, false), Times.Once);
        host.Users.Verify(value => value.ResetAuthenticatorKeyAsync(User), Times.Once);
    }
}
