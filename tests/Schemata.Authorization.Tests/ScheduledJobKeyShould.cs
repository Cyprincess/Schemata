using Schemata.Authorization.Foundation.Services;
using Schemata.Security.Skeleton.Entities;
using Schemata.Scheduling.Foundation.Runtime;
using Xunit;

namespace Schemata.Authorization.Tests;

public class ScheduledJobKeyShould
{
    private static readonly char[] Unaddressable = ['`', '[', ']', ',', '=', ' ', '/'];

    [Fact]
    public void Keep_The_Token_Cleanup_Key_Within_The_Persisted_Column() {
        var registry = new DefaultScheduledJobRegistry();
        registry.RegisterAll([typeof(TokenCleanupJob<SchemataToken>)]);

        var key = registry.ResolveKey(typeof(TokenCleanupJob<SchemataToken>))!;

        Assert.Equal("schemata.authorization.token.cleanup", key);
        Assert.True(key.Length <= 128, $"Key is {key.Length} characters: {key}");
        Assert.Equal(-1, key.IndexOfAny(Unaddressable));
    }

    [Fact]
    public void Resolve_The_Token_Cleanup_Job_Back_From_Its_Persisted_Key() {
        var registry = new DefaultScheduledJobRegistry();
        registry.RegisterAll([typeof(TokenCleanupJob<SchemataToken>)]);

        Assert.Equal(typeof(TokenCleanupJob<SchemataToken>),
                     registry.Resolve("schemata.authorization.token.cleanup"));
    }

    [Fact]
    public void Keep_The_Back_Channel_Logout_Key_Addressable() {
        var registry = new DefaultScheduledJobRegistry();
        registry.RegisterAll([typeof(BackChannelLogoutJob<SchemataSecurity>)]);

        var key = registry.ResolveKey(typeof(BackChannelLogoutJob<SchemataSecurity>))!;

        Assert.Equal("schemata.authorization.logout.backchannel", key);
        Assert.Equal(typeof(BackChannelLogoutJob<SchemataSecurity>), registry.Resolve(key));
        Assert.Equal(-1, key.IndexOfAny(Unaddressable));
    }
}
