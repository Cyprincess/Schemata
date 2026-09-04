using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Actor.Foundation.Tests.Fixtures;
using Xunit;

namespace Schemata.Actor.Foundation.Tests;

public class AmbientTurnShould
{
    [Fact]
    public async Task AmbientTurn_DuringOnReceive_CurrentIsTheTurnsOwnAdviceContext_AndRestoredAfterward() {
        var (system, _, _) = ActorSystemFactory.Create();
        var actor           = await system.SpawnAsync(new("scope-probe", "b"), new(typeof(ScopeProbeActor)));

        Assert.Null(AdviceContext.Current);

        var observedFromInsideTheTurn = await actor.AskAsync<CaptureAmbient, bool>(new());

        Assert.True(observedFromInsideTheTurn);
        Assert.Null(AdviceContext.Current);
    }
}
