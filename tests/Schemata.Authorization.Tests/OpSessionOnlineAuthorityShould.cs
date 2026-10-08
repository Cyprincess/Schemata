using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Services;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using Xunit;

namespace Schemata.Authorization.Tests;

public class OpSessionOnlineAuthorityShould
{
    private static readonly DateTimeOffset Now = new(2026, 9, 23, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Establish_One_Generation_For_The_Configured_Slot_Lifetime() {
        var (service, tokens, _) = Create();
        tokens.Setup(t => t.GetOrCreateAsync(
                   "users/u-1", "op-session", "sid-1", null, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(Slot("gen-1", Now + TimeSpan.FromHours(2)));

        var generation = await service.EstablishOnlineAsync("users/u-1", "sid-1");

        Assert.Equal("gen-1", generation);
        tokens.Verify(t => t.GetOrCreateAsync(
                          "users/u-1", "op-session", "sid-1", null,
                          TimeSpan.FromHours(2), It.IsAny<CancellationToken>()),
                      Times.Once);
    }

    [Fact]
    public async Task Reuse_The_Live_Generation_Until_The_Slot_Is_Invalidated() {
        var (service, tokens, _) = Create();
        SchemataToken? slot = null;
        var minted = 0;
        tokens.Setup(t => t.GetOrCreateAsync(
                   "users/u-1", "op-session", "sid-1", null, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(() => slot ??= Slot($"gen-{++minted}", Now + TimeSpan.FromHours(2)));
        tokens.Setup(t => t.RemoveAsync("users/u-1", "op-session", "sid-1", It.IsAny<CancellationToken>()))
              .Callback(() => slot = null)
              .Returns(Task.CompletedTask);

        var first  = await service.EstablishOnlineAsync("users/u-1", "sid-1");
        var reused = await service.EstablishOnlineAsync("users/u-1", "sid-1");

        await service.InvalidateAsync(null, "users/u-1", "sid-1");
        var reestablished = await service.EstablishOnlineAsync("users/u-1", "sid-1");

        Assert.Equal("gen-1", first);
        Assert.Equal("gen-1", reused);
        Assert.Equal("gen-2", reestablished);
        tokens.Verify(t => t.RemoveAsync("users/u-1", "op-session", "sid-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Validate_Only_The_Exact_Generation_Of_A_Live_Slot() {
        var (service, tokens, _) = Create();
        tokens.Setup(t => t.GetAsync("users/u-1", "op-session", "sid-1", It.IsAny<CancellationToken>()))
              .ReturnsAsync(Slot("gen-1", Now + TimeSpan.FromHours(2)));

        Assert.True(await service.ValidateOnlineAsync("users/u-1", "sid-1", "gen-1"));
        Assert.False(await service.ValidateOnlineAsync("users/u-1", "sid-1", "gen-2"));
        Assert.False(await service.ValidateOnlineAsync("users/u-1", "sid-1", null));
    }

    [Fact]
    public async Task Fail_Validation_When_The_Slot_Expired_Or_Is_Missing() {
        var (service, tokens, _) = Create();
        tokens.Setup(t => t.GetAsync("users/u-1", "op-session", "sid-expired", It.IsAny<CancellationToken>()))
              .ReturnsAsync(Slot("gen-1", Now - TimeSpan.FromMinutes(1), "sid-expired"));

        Assert.False(await service.ValidateOnlineAsync("users/u-1", "sid-expired", "gen-1"));
        Assert.False(await service.ValidateOnlineAsync("users/u-1", "sid-missing", "gen-1"));
    }

    [Fact]
    public async Task Treat_A_Store_Managed_Slot_Without_Expiry_As_Live() {
        // Cache-backed slots expire by eviction and read back without an ExpireTime.
        var (service, tokens, _) = Create();
        tokens.Setup(t => t.GetAsync("users/u-1", "op-session", "sid-1", It.IsAny<CancellationToken>()))
              .ReturnsAsync(new SchemataToken {
                  Parent = "users/u-1", Provider = "op-session", Key = "sid-1", Value = "gen-1",
              });

        Assert.True(await service.ValidateOnlineAsync("users/u-1", "sid-1", "gen-1"));
    }

    [Fact]
    public async Task Fail_Closed_Without_A_Token_Store() {
        var (service, _, _) = Create(withStore: false);

        Assert.Null(await service.EstablishOnlineAsync("users/u-1", "sid-1"));
        Assert.False(await service.ValidateOnlineAsync("users/u-1", "sid-1", "gen-1"));
    }

    [Fact]
    public async Task Fail_Closed_For_A_Blank_Subject_Session_Or_Generation() {
        var (service, tokens, _) = Create();

        Assert.Null(await service.EstablishOnlineAsync(null, "sid-1"));
        Assert.Null(await service.EstablishOnlineAsync("users/u-1", " "));
        Assert.False(await service.ValidateOnlineAsync(null, "sid-1", "gen-1"));
        Assert.False(await service.ValidateOnlineAsync("users/u-1", null, "gen-1"));
        Assert.False(await service.ValidateOnlineAsync("users/u-1", "sid-1", null));
        Assert.False(await service.ValidateOnlineAsync("users/u-1", "sid-1", " "));
        tokens.Verify(t => t.GetOrCreateAsync(
                          It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(),
                          It.IsAny<string?>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
                      Times.Never);
        tokens.Verify(t => t.GetAsync(
                          It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
                      Times.Never);
    }

    private static (
        DefaultOpSessionService Service,
        Mock<ITokenStore<SchemataToken>> Tokens,
        FakeTimeProvider Clock
    ) Create(bool withStore = true) {
        var clock  = new FakeTimeProvider(Now);
        var tokens = new Mock<ITokenStore<SchemataToken>>();
        var service = new DefaultOpSessionService(
            Options.Create(new SchemataAuthorizationOptions { OnlineSessionLifetime = TimeSpan.FromHours(2) }),
            null,
            withStore ? tokens.Object : null,
            clock);
        return (service, tokens, clock);
    }

    private static SchemataToken Slot(string value, DateTimeOffset expiry, string key = "sid-1") {
        return new() {
            Parent     = "users/u-1",
            Provider   = "op-session",
            Key        = key,
            Value      = value,
            ExpireTime = expiry.UtcDateTime,
        };
    }
}
