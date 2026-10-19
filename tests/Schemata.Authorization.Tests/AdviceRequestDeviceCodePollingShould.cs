using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Common.Errors;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using Xunit;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

public class AdviceRequestDeviceCodePollingShould
{
    private const string Device = "device-code";

    [Fact]
    public async Task Admit_The_First_Poll_For_The_Configured_Window() {
        var (slots, ttls)   = Slots();
        var grants          = Grants(Device);
        var advisor         = Advisor(slots, grants);

        var result = await advisor.AdviseAsync(new(null!), new() { Request = Request() });

        Assert.Equal(AdviseResult.Continue, result);
        var window = Assert.Single(ttls);
        Assert.Equal(TimeSpan.FromSeconds(5), window);
        grants.Verify(value => value.UpdateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>(),
            It.IsAny<Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>?>()), Times.Never);
    }

    [Fact]
    public async Task Reject_A_Second_Poll_Of_The_Same_Window_And_Grow_The_Grant_Interval() {
        var slots           = SlotStoreWithWinner();
        var grants          = Grants(Device);
        var advisor         = Advisor(slots, grants);

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            advisor.AdviseAsync(new(null!), new() { Request = Request() }));

        Assert.Equal(OAuthErrors.SlowDown, exception.Status);
        grants.Verify(value => value.UpdateAsync(
            It.Is<SchemataToken>(grant => grant.Value == "10"),
            It.IsAny<CancellationToken>(),
            It.IsAny<Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>?>()), Times.Once);
    }

    [Fact]
    public async Task Admit_A_Poll_After_The_Window_Elapsed_Using_The_Persisted_Grown_Interval() {
        var (slots, ttls) = Slots();
        var grants        = Grants(Device, "10");
        var advisor       = Advisor(slots, grants);

        var result = await advisor.AdviseAsync(new(null!), new() { Request = Request() });

        Assert.Equal(AdviseResult.Continue, result);
        var window = Assert.Single(ttls);
        Assert.Equal(TimeSpan.FromSeconds(10), window);
    }

    [Fact]
    public async Task Ignore_A_Unknown_Device_Code() {
        var (slots, ttls) = Slots();
        var grants        = new Mock<ITokenStore<SchemataToken>>();
        grants.Setup(value => value.FindByReferenceIdAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync((SchemataToken?)null);
        var advisor = Advisor(slots, grants);

        var result = await advisor.AdviseAsync(new(null!), new() { Request = Request() });

        Assert.Equal(AdviseResult.Continue, result);
        Assert.Empty(ttls);
    }

    [Fact]
    public async Task Swallow_Only_A_Token_Conflict_Abort_From_The_Interval_Widen() {
        var conflict = Grants(Device);
        conflict.Setup(value => value.UpdateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>(),
                          It.IsAny<Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>?>()))
                .ThrowsAsync(SchemataResourceErrors.Aborted<SchemataToken>());

        var slowDown = await Assert.ThrowsAsync<OAuthException>(
            () => Advisor(SlotStoreWithWinner(), conflict).AdviseAsync(new(null!), new() { Request = Request() }));
        Assert.Equal(OAuthErrors.SlowDown, slowDown.Status);

        var unrelated = Grants(Device);
        unrelated.Setup(value => value.UpdateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>(),
                            It.IsAny<Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>?>()))
                 .ThrowsAsync(new AbortedException());

        await Assert.ThrowsAsync<AbortedException>(
            () => Advisor(SlotStoreWithWinner(), unrelated).AdviseAsync(new(null!), new() { Request = Request() }));
    }

    private static AdviceRequestDeviceCodePolling<SchemataApplication, SchemataToken> Advisor(
        Mock<ITokenStore<SchemataToken>> slots, Mock<ITokenStore<SchemataToken>> grants) {
        return new(slots.Object, grants.Object, Options.Create(new SchemataAuthorizationOptions { DeviceCodeInterval = 5 }));
    }

    private static TokenRequest Request() {
        return new() { GrantType = GrantTypes.DeviceCode, DeviceCode = Device };
    }

    private static Mock<ITokenStore<SchemataToken>> Grants(string reference, string? grownInterval = null) {
        var grant = new SchemataToken {
            Type        = TokenTypes.DeviceCode,
            ReferenceId = reference,
            Value       = grownInterval,
        };
        var grants = new Mock<ITokenStore<SchemataToken>>();
        grants.Setup(value => value.FindByReferenceIdAsync(reference, It.IsAny<CancellationToken>()))
              .ReturnsAsync(grant);
        grants.Setup(value => value.UpdateAsync(It.IsAny<SchemataToken>(), It.IsAny<CancellationToken>(),
                          It.IsAny<Func<Schemata.Entity.Repository.IUnitOfWork, CancellationToken, Task>?>()))
              .Returns(Task.CompletedTask);
        return grants;
    }

    private static (
        Mock<ITokenStore<SchemataToken>> Store,
        List<TimeSpan> Ttls
        ) Slots() {
        var ttls       = new List<TimeSpan>();
        var (store, _) = SlotStore(ttls);
        return (store, ttls);
    }

    private static Mock<ITokenStore<SchemataToken>> SlotStoreWithWinner() {
        var (store, _) = SlotStore(new(), preexisting: true);
        return store;
    }

    private static (
        Mock<ITokenStore<SchemataToken>> Store,
        List<TimeSpan> Ttls
        ) SlotStore(List<TimeSpan> ttls, bool preexisting = false) {
        SchemataToken? established = preexisting
            ? new() { Parent = null, Provider = "device", Key = $"rate:{Device}", Value = "winner-ticket" }
            : null;
        var mock = new Mock<ITokenStore<SchemataToken>>();
        mock.Setup(value => value.GetOrCreateAsync(
                        It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                        It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string? parent, string provider, string key, string? candidate, TimeSpan ttl, CancellationToken _) => {
                if (established is not null) {
                    return established;
                }

                established = new() {
                    Parent     = parent,
                    Provider   = provider,
                    Key        = key,
                    Value      = candidate,
                    ExpireTime = DateTime.UtcNow + ttl,
                };
                ttls.Add(ttl);
                return established;
            });
        return (mock, ttls);
    }
}
