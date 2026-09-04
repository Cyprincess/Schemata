using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using Xunit;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

public class AdviceRequestDeviceCodePollingShould
{
    private const string Provider = "device";
    private const string Name     = "rate:device-code";

    [Fact]
    public async Task First_Poll_Stores_Configured_Interval() {
        var (slots, rows, ttls) = Slots();
        var advisor = Advisor(slots);

        var result = await advisor.AdviseAsync(new(null!), new(), Request());

        Assert.Equal(AdviseResult.Continue, result);
        Assert.Equal("5", rows[(null, Provider, Name)].Value);
        Assert.Equal(TimeSpan.FromSeconds(5), Assert.Single(ttls));
    }

    [Fact]
    public async Task Early_Poll_Returns_SlowDown_And_Grows_Interval_By_Five_Seconds() {
        var (slots, rows, ttls) = Slots(new() {
            [(null, Provider, Name)] = new() { Parent = null, Provider = Provider, Name = Name, Value = "5" },
        });
        var advisor = Advisor(slots);

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            advisor.AdviseAsync(new(null!), new(), Request()));

        Assert.Equal(OAuthErrors.SlowDown, exception.Status);
        Assert.Equal("10", rows[(null, Provider, Name)].Value);
        Assert.Equal(TimeSpan.FromSeconds(10), Assert.Single(ttls));
    }

    private static AdviceRequestDeviceCodePolling<SchemataApplication> Advisor(Mock<ITokenStore<SchemataToken>> slots) {
        return new(slots.Object, Options.Create(new SchemataAuthorizationOptions { DeviceCodeInterval = 5 }));
    }

    private static TokenRequest Request() {
        return new() { GrantType = GrantTypes.DeviceCode, DeviceCode = "device-code" };
    }

    private static (
        Mock<ITokenStore<SchemataToken>>                          Store,
        Dictionary<(string?, string, string), SchemataToken>      Rows,
        List<TimeSpan>                                            Ttls
    ) Slots(Dictionary<(string?, string, string), SchemataToken>? seed = null) {
        var rows = seed ?? [];
        var ttls = new List<TimeSpan>();
        var mock = new Mock<ITokenStore<SchemataToken>>();
        mock.Setup(value => value.GetAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string? parent, string provider, string name, CancellationToken _) =>
                rows.TryGetValue((parent, provider, name), out var row) ? row : null);
        mock.Setup(value => value.SetAsync(
                        It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                        It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
            .Callback((string? parent, string provider, string name, string? value, TimeSpan? ttl, CancellationToken _) => {
                rows[(parent, provider, name)] = new() {
                    Parent     = parent,
                    Provider   = provider,
                    Name       = name,
                    Value      = value,
                    ExpireTime = ttl is null ? null : DateTime.UtcNow + ttl.Value,
                };
                if (ttl is not null) {
                    ttls.Add(ttl.Value);
                }
            })
            .Returns(Task.CompletedTask);
        mock.Setup(value => value.GetOrCreateAsync(
                        It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                        It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string? parent, string provider, string name, string? value, TimeSpan ttl, CancellationToken _) => {
                if (!rows.TryGetValue((parent, provider, name), out var row)) {
                    row = new() {
                        Parent     = parent,
                        Provider   = provider,
                        Name       = name,
                        Value      = value,
                        ExpireTime = DateTime.UtcNow + ttl,
                    };
                    rows[(parent, provider, name)] = row;
                }

                ttls.Add(ttl);
                return row;
            });
        return (mock, rows, ttls);
    }
}
