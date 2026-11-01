using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Schemata.Core;
using Schemata.Push.Actor.Features;
using Schemata.Push.Foundation.Features;
using Schemata.Push.Skeleton.Entities;
using Xunit;

namespace Schemata.Push.Actor.Tests;

/// <summary>
///     Proves the actor bridge is installed from the closed push builder, so the Push runtime the
///     bridge serializes is always present.
/// </summary>
public sealed class PushActorCompositionShould
{
    [Fact]
    public void UseActor_Closes_The_Bridge_On_The_Selected_Subscription() {
        var builder = new SchemataBuilder(new ConfigurationBuilder().Build(), null!);
        builder.UsePush<ActorSubscription>().UseActor();

        Assert.True(builder.HasFeature<SchemataPushFeature<ActorSubscription>>());
        Assert.True(builder.HasFeature<SchemataPushFeature>());
        Assert.True(builder.HasFeature<SchemataPushActorFeature>());
    }

    private sealed class ActorSubscription : SchemataPushSubscription;
}
