using System.Threading;
using System.Threading.Tasks;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Xunit;

namespace Schemata.Flow.Bpmn.Tests;

public class BpmnEngine_CompensationBoundaryRegistrationShould
{
    [Fact]
    public async Task Engine_ActivityWithCompensationBoundaryCompletes_ThrowFiresRegisteredHandler() {
        var scenario = CompensationScenario(true);
        var process  = NewProcess(scenario.Definition.Name);
        var engine   = new BpmnEngine();

        var started = await engine.StartAsync(scenario.Definition, process, CancellationToken.None);
        var thrown  = await engine.AdvanceAsync(scenario.Definition, started.Process, started.Tokens, null, CancellationToken.None);

        var compensation = Assert.Single(thrown.Transitions, t => t.Kind == TransitionKind.Compensate);
        Assert.Equal(scenario.Host.Name, compensation.Previous);
        Assert.Equal(scenario.CompensationTarget.Name, compensation.Posterior);
        Assert.Equal("compensate-host", compensation.Event);
    }

    [Fact]
    public async Task Engine_ScopeExitNormalNoCompensation_ClearsScopeStack() {
        var scenario = CompensationScenario(false);
        var process  = NewProcess(scenario.Definition.Name);
        var engine   = new BpmnEngine();

        var started   = await engine.StartAsync(scenario.Definition, process, CancellationToken.None);
        var afterHost = await engine.AdvanceAsync(scenario.Definition, started.Process, started.Tokens, null, CancellationToken.None);
        var completed = await engine.AdvanceAsync(scenario.Definition, afterHost.Process, afterHost.Tokens, null, CancellationToken.None);

        Assert.Equal("Completed", completed.Process.State);
        Assert.DoesNotContain(completed.Transitions, t => t.Kind == TransitionKind.Compensate);
    }

    private static CompensationRegistrationScenario CompensationScenario(bool throwAfterHost) {
        var start = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var host  = new NoneTask { Name = "host" };
        FlowElement next = throwAfterHost
            ? new FlowEvent {
                Name         = "throw",
                Position   = EventPosition.IntermediateThrow,
                Definition = new CompensationDefinition { Name = "compensate-host", Activity = host },
            }
            : new NoneTask { Name = "next" };
        var end   = new FlowEvent { Name = "end", Position = EventPosition.End };
        var boundary = new FlowEvent {
            Name         = "compensate-host",
            Position   = EventPosition.Boundary,
            AttachedTo = host,
            Definition = new CompensationDefinition { Name = "compensate-host", Activity = host },
        };
        var compensationTarget = new NoneTask { Name = "undo-host" };
        var definition = new ProcessDefinition {
            Name     = "compensation-registration",
            Elements = { start, host, next, end, boundary, compensationTarget },
            Flows = {
                new() { Source = start, Target = host },
                new() { Source = host, Target = next },
                new() { Source = next, Target = end },
                new() { Source = boundary, Target = compensationTarget },
            },
        };

        return new(definition, host, compensationTarget);
    }

    private static SchemataProcess NewProcess(string definitionName) {
        return new() {
            Name           = "p1",
            CanonicalName  = "processes/p1",
            DefinitionName = definitionName,
        };
    }

    private sealed record CompensationRegistrationScenario(
        ProcessDefinition Definition,
        Activity          Host,
        Activity          CompensationTarget);
}
