using System;
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
using Schemata.Caching.Skeleton;
using Xunit;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

public class AdviceRequestDeviceCodePollingShould
{
    [Fact]
    public async Task First_Poll_Stores_Configured_Interval() {
        var cache = new Mock<ICacheProvider>();
        cache.Setup(value => value.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((byte[]?)null);
        byte[]? stored = null;
        CacheEntryOptions? entry = null;
        cache.Setup(value => value.SetAsync(
                        It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CacheEntryOptions>(),
                        It.IsAny<CancellationToken>()))
             .Callback((string _, byte[] value, CacheEntryOptions options, CancellationToken _) => {
                 stored = value;
                 entry  = options;
             })
             .Returns(Task.CompletedTask);
        var advisor = Advisor(cache);

        var result = await advisor.AdviseAsync(new(null!), new SchemataApplication(), Request());

        Assert.Equal(AdviseResult.Continue, result);
        Assert.Equal(5, BitConverter.ToInt32(stored!));
        Assert.Equal(TimeSpan.FromSeconds(5), entry!.AbsoluteExpirationRelativeToNow);
    }

    [Fact]
    public async Task Early_Poll_Returns_SlowDown_And_Grows_Interval_By_Five_Seconds() {
        var cache = new Mock<ICacheProvider>();
        cache.Setup(value => value.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(BitConverter.GetBytes(5));
        byte[]? stored = null;
        CacheEntryOptions? entry = null;
        cache.Setup(value => value.SetAsync(
                        It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CacheEntryOptions>(),
                        It.IsAny<CancellationToken>()))
             .Callback((string _, byte[] value, CacheEntryOptions options, CancellationToken _) => {
                 stored = value;
                 entry  = options;
             })
             .Returns(Task.CompletedTask);
        var advisor = Advisor(cache);

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            advisor.AdviseAsync(new(null!), new SchemataApplication(), Request()));

        Assert.Equal(OAuthErrors.SlowDown, exception.Status);
        Assert.Equal(10, BitConverter.ToInt32(stored!));
        Assert.Equal(TimeSpan.FromSeconds(10), entry!.AbsoluteExpirationRelativeToNow);
    }

    private static AdviceRequestDeviceCodePolling<SchemataApplication> Advisor(Mock<ICacheProvider> cache) {
        return new(cache.Object, Options.Create(new SchemataAuthorizationOptions { DeviceCodeInterval = 5 }));
    }

    private static TokenRequest Request() {
        return new() { GrantType = GrantTypes.DeviceCode, DeviceCode = "device-code" };
    }
}
