using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Identity.Skeleton;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Tests.Fixtures;
using Xunit;

namespace Schemata.Identity.Tests;

[Trait("Layer", "Unit")]
public class RefreshTicketShould
{
    [Theory]
    [InlineData(null)]
    [InlineData(-1)]
    [InlineData(0)]
    public async Task Reject_Expired_Or_Unbounded_Ticket_Before_Renewal(int? secondsRemaining) {
        var now = new DateTimeOffset(2026, 9, 26, 0, 0, 0, TimeSpan.Zero);
        var clock = new Mock<TimeProvider>();
        clock.Setup(t => t.GetUtcNow()).Returns(now);
        using var host = new IdentityHandlerTestHost(services => services.AddSingleton(clock.Object));
        var principal = new ClaimsPrincipal(new ClaimsIdentity("refresh"));
        var ticket = new AuthenticationTicket(principal, new AuthenticationProperties {
            ExpiresUtc = secondsRemaining is { } seconds ? now.AddSeconds(seconds) : null,
        }, "refresh");

        var result = await host.Handler.RefreshAsync(ticket, new ClaimsPrincipal(), CancellationToken.None);

        Assert.Equal(IdentityStatus.Challenge, result.Status);
        Assert.Null(result.Data);
        host.SignIn.Verify(s => s.ValidateSecurityStampAsync(It.IsAny<ClaimsPrincipal>()), Times.Never);
        host.SignIn.Verify(s => s.CreateUserPrincipalAsync(It.IsAny<SchemataUser>()), Times.Never);
    }

    [Fact]
    public async Task Renew_Live_Ticket_With_Original_Authentication_Event() {
        var now = new DateTimeOffset(2026, 9, 26, 0, 0, 0, TimeSpan.Zero);
        var clock = new Mock<TimeProvider>();
        clock.Setup(t => t.GetUtcNow()).Returns(now);
        using var host = new IdentityHandlerTestHost(services => services.AddSingleton(clock.Object));
        var user = new SchemataUser();
        var original = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("auth_time", "1720000000"),
            new Claim("acr", "mfa"),
            new Claim("amr", "[\"pwd\",\"otp\",\"mfa\"]", "JSON"),
        ], "refresh"));
        var renewed = new ClaimsPrincipal(new ClaimsIdentity("renewed"));
        host.SignIn.Setup(s => s.ValidateSecurityStampAsync(original)).ReturnsAsync(user);
        host.SignIn.Setup(s => s.CreateUserPrincipalAsync(user)).ReturnsAsync(renewed);
        var ticket = new AuthenticationTicket(original, new AuthenticationProperties {
            ExpiresUtc = now.AddSeconds(1),
        }, "refresh");

        var result = await host.Handler.RefreshAsync(ticket, new ClaimsPrincipal(), CancellationToken.None);

        Assert.Equal(IdentityStatus.Success, result.Status);
        Assert.NotNull(result.Data);
        Assert.Equal("1720000000", result.Data.FindFirst("auth_time")?.Value);
        Assert.Equal("mfa", result.Data.FindFirst("acr")?.Value);
        Assert.Equal("[\"pwd\",\"otp\",\"mfa\"]", result.Data.FindFirst("amr")?.Value);
    }
}
