using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Schemata.Core;
using Schemata.Push.Foundation.Features;
using Schemata.Push.Http.Features;
using Schemata.Push.Skeleton.Entities;
using Xunit;

namespace Schemata.Push.Http.Integration.Tests;

/// <summary>
///     Proves <c>MapHttp()</c> installs the HTTP feature closed on the subscription entity the Push
///     entry selected.
/// </summary>
public sealed class CustomPushHttpFeatureShould
{
    [Fact]
    public void MapHttp_Closes_The_Transport_On_The_Selected_Subscription() {
        var builder = new SchemataBuilder(new ConfigurationBuilder().Build(), null!);
        builder.UsePush<TransportSubscription>().MapHttp();

        Assert.True(builder.HasFeature<SchemataPushHttpFeature<TransportSubscription>>());
        Assert.True(builder.HasFeature<SchemataPushFeature>());
        Assert.False(builder.HasFeature<SchemataPushHttpFeature<SchemataPushSubscription>>());
    }

    [Fact]
    public void MapHttp_For_The_Default_Entry_Closes_The_Transport_On_The_Built_In_Subscription() {
        var builder = new SchemataBuilder(new ConfigurationBuilder().Build(), null!);
        builder.UsePush().MapHttp();

        Assert.True(builder.HasFeature<SchemataPushHttpFeature<SchemataPushSubscription>>());
    }

    private sealed class TransportSubscription : SchemataPushSubscription;
}
