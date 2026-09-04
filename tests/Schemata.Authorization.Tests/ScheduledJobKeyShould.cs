using Schemata.Authorization.Foundation.Services;
using Schemata.Security.Skeleton.Entities;
using Schemata.Scheduling.Foundation.Runtime;
using Xunit;

namespace Schemata.Authorization.Tests;

public class ScheduledJobKeyShould
{
    private static readonly char[] Unaddressable = ['`', '[', ']', ',', '=', ' ', '/'];

    [Fact]
    public void KeepTheTokenCleanupKeyWithinThePersistedColumn() {
        var registry = new DefaultScheduledJobRegistry();
        registry.RegisterAll([typeof(TokenCleanupJob)]);

        var key = registry.ResolveKey(typeof(TokenCleanupJob))!;

        Assert.Equal("schemata.authorization.token.cleanup", key);
        Assert.True(key.Length <= 128, $"Key is {key.Length} characters: {key}");
        Assert.Equal(-1, key.IndexOfAny(Unaddressable));
    }

    [Fact]
    public void ResolveTheTokenCleanupJobBackFromItsPersistedKey() {
        var registry = new DefaultScheduledJobRegistry();
        registry.RegisterAll([typeof(TokenCleanupJob)]);

        Assert.Equal(typeof(TokenCleanupJob),
                     registry.Resolve("schemata.authorization.token.cleanup"));
    }

    [Fact]
    public void KeepTheBackChannelLogoutKeyAddressable() {
        var registry = new DefaultScheduledJobRegistry();
        registry.RegisterAll([typeof(BackChannelLogoutJob)]);

        var key = registry.ResolveKey(typeof(BackChannelLogoutJob))!;

        Assert.Equal("schemata.authorization.logout.backchannel", key);
        Assert.Equal(typeof(BackChannelLogoutJob), registry.Resolve(key));
        Assert.Equal(-1, key.IndexOfAny(Unaddressable));
    }
}
