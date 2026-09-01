using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Actor.Foundation.Tests.Fixtures;
using Schemata.Actor.Skeleton;
using Xunit;

namespace Schemata.Actor.Foundation.Tests;

public class TurnScopeShould
{
    [Fact]
    public async Task TurnScope_AcrossThreeTurns_ResolvesADifferentScopedInstanceEachTime_AndDisposesEachOnceItsTurnEnds() {
        var (system, _, root) = ActorSystemFactory.Create(services => {
            services.AddSingleton<MarkerRegistry>();
            services.AddScoped<ScopedMarker>();
        });
        var actor = await system.SpawnAsync(new ActorId("scope-probe", "a"), new Props(typeof(ScopeProbeActor)));

        var id1 = await actor.AskAsync<GetMarkerId, Guid>(new GetMarkerId());
        var id2 = await actor.AskAsync<GetMarkerId, Guid>(new GetMarkerId());
        // A third turn's completion guarantees the second turn's own scope has already been
        // released - the single-consumer mailbox loop cannot start turn N+1 until turn N's whole
        // "await using" scope block, disposal included, has returned.
        await actor.AskAsync<GetMarkerId, Guid>(new GetMarkerId());

        Assert.NotEqual(id1, id2);

        var registry = root.GetRequiredService<MarkerRegistry>();
        var marker1  = registry.Instances.Single(m => m.Id == id1);
        var marker2  = registry.Instances.Single(m => m.Id == id2);

        Assert.True(marker1.Disposed);
        Assert.True(marker2.Disposed);
    }
}
