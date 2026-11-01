using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Actor.Event.Features;
using Schemata.Actor.Event.Tests.Fixtures;
using Schemata.Actor.Foundation.Features;
using Schemata.Actor.Foundation.Runtime;
using Schemata.Actor.Skeleton;
using Schemata.Actor.Skeleton.Entities;
using Schemata.Core;
using Schemata.Messaging.Skeleton;
using Xunit;

namespace Schemata.Actor.Event.Tests;

/// <summary>
///     A bridge feature depends on the non-generic <see cref="SchemataActorFeature" /> marker, so a
///     bridge staged before <c>UseActor&lt;TActor&gt;()</c> leaves the persisted actor-state entity
///     choice to the host instead of auto-registering the default closed feature.
/// </summary>
public class ActorEntityChoiceShould
{
    [Trait("Layer", "Component")]
    [Fact]
    public async Task Bridge_Staged_Before_UseActor_Keeps_The_Host_Defined_Actor_Entity() {
        var builder = new SchemataBuilder(new ConfigurationBuilder().Build(), null!);
        builder.AddFeature<SchemataActorEventFeature>();
        builder.UseActor<SessionActor>(actor => actor.Register<RecordingActor>("recorder"));

        Assert.False(builder.HasFeature<SchemataActorFeature<SchemataActor>>());

        var services = new ServiceCollection();
        builder.Invoke(services);
        await using var root = services.BuildServiceProvider();

        var system = root.GetRequiredService<IActorSystem>();
        Assert.IsType<InProcessActorSystem<SessionActor>>(system);

        var actor = await system.GetAsync(new("recorder", "entity-choice"));
        Assert.Null(await actor.AskAsync<GetReceived, IMessage?>(new()));
        await ((IActorSystemLifetime)system).ShutdownAsync(CancellationToken.None);
    }

    public sealed class SessionActor : SchemataActor;
}
