using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;
using Xunit;

namespace Schemata.Flow.Bpmn.Tests;

public class BpmnEngineComplexGatewayShould
{
    [Fact]
    public async Task DirectStart_WithoutActivationCondition_ForksSelectedBranches() {
        var definition = ComplexDefinition();
        var process    = NewProcess(definition.Name);
        var engine     = new BpmnEngine();

        var started = await engine.StartAsync(definition, process, CancellationToken.None);

        var parent = Assert.Single(started.Tokens, t => t.State == "Completed");
        Assert.Equal("complex", parent.StateName);
        Assert.Null(parent.WaitingAtName);

        var children = started.Tokens.Where(t => t.Spawner == parent.CanonicalName).ToList();
        Assert.Equal(2, children.Count);
        Assert.Equal(["task-a", "task-b"], children.Select(t => t.StateName).OrderBy(n => n));
        Assert.All(children, t => Assert.Equal("Active", t.State));

        Assert.Equal(3, started.Tokens.Count);
        Assert.Equal(3, started.Transitions.Count);
        Assert.Single(started.Transitions, t => t.Kind == TransitionKind.Fork);
        Assert.Equal(2, started.Transitions.Count(t => t.Kind == TransitionKind.Move));
        Assert.Equal("Running", started.Process.State);
    }

    [Fact]
    public async Task DirectStart_WhenUnready_ParksAtGateway() {
        var definition = ComplexDefinition(Const(false));
        var process    = NewProcess(definition.Name);
        var engine     = new BpmnEngine();

        var parked = await engine.StartAsync(definition, process, CancellationToken.None);

        var waiting = Assert.Single(parked.Tokens);
        Assert.Equal("Waiting", waiting.State);
        Assert.Equal("complex", waiting.StateName);
        Assert.Equal("complex", waiting.WaitingAtName);
        Assert.Null(waiting.Spawner);

        var arrival = Assert.Single(parked.Transitions);
        Assert.Equal(TransitionKind.Move, arrival.Kind);
        Assert.Equal("Advance", arrival.Event);
        Assert.Null(arrival.Previous);
        Assert.Equal("complex", arrival.Posterior);
        Assert.DoesNotContain(parked.Transitions, t => t.Kind == TransitionKind.Fork);
        Assert.Equal("Waiting", parked.Process.State);
    }

    [Fact]
    public async Task RepeatedAdvance_WhenStillUnready_ReturnsWaitingSnapshot() {
        var definition = ComplexDefinition(Const(false));
        var process    = NewProcess(definition.Name);
        var engine     = new BpmnEngine();
        var parked     = await engine.StartAsync(definition, process, CancellationToken.None);
        var token      = Assert.Single(parked.Tokens);

        var repeated = await engine.AdvanceAsync(definition, parked.Process, parked.Tokens, token.CanonicalName, CancellationToken.None);

        var waiting = Assert.Single(repeated.Tokens);
        Assert.Equal("Waiting", waiting.State);
        Assert.Equal("complex", waiting.StateName);
        Assert.Equal("complex", waiting.WaitingAtName);
        Assert.Empty(repeated.Transitions);
        Assert.Empty(repeated.Tokens.Skip(1));
        Assert.DoesNotContain(repeated.Transitions, t => t.Kind == TransitionKind.Fork);
    }

    [Fact]
    public async Task Advance_WhenReadinessFlips_ForksOnlyOnce() {
        var ready      = false;
        var activation = new LambdaConditionExpression { Lambda = _ => new(ready) };
        var definition = ComplexDefinition(activation, selectB: false);
        var process    = NewProcess(definition.Name);
        var engine     = new BpmnEngine();
        var parked     = await engine.StartAsync(definition, process, CancellationToken.None);
        var token      = Assert.Single(parked.Tokens);

        ready = true;
        var advanced = await engine.AdvanceAsync(definition, parked.Process, parked.Tokens, token.CanonicalName, CancellationToken.None);

        var parent = Assert.Single(advanced.Tokens, t => t.State == "Completed");
        var child  = Assert.Single(advanced.Tokens, t => t.Spawner == parent.CanonicalName);
        Assert.Equal("task-a", child.StateName);
        Assert.Equal("Active", child.State);
        Assert.Equal(2, advanced.Tokens.Count);
        Assert.Equal(2, advanced.Transitions.Count);
        Assert.Single(advanced.Transitions, t => t.Kind == TransitionKind.Fork);
        Assert.Single(advanced.Transitions, t => t.Kind == TransitionKind.Move);
    }

    private static ProcessDefinition ComplexDefinition(
        IConditionExpression? activation = null,
        bool selectA = true,
        bool selectB = true
    ) {
        var start   = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var complex = new ComplexGateway { Name = "complex", ActivationCount = activation };
        var a       = new NoneTask { Name = "task-a" };
        var b       = new NoneTask { Name = "task-b" };
        var endA    = new FlowEvent { Name = "end-a", Position = EventPosition.End };
        var endB    = new FlowEvent { Name = "end-b", Position = EventPosition.End };

        return new() {
            Name     = "complex-gateway",
            Elements = { start, complex, a, b, endA, endB },
            Flows = {
                new() { Source = start, Target = complex },
                new() { Source = complex, Target = a, Condition = Const(selectA) },
                new() { Source = complex, Target = b, Condition = Const(selectB) },
                new() { Source = a, Target = endA },
                new() { Source = b, Target = endB },
            },
        };
    }

    private static LambdaConditionExpression Const(bool value) {
        return new() { Lambda = _ => new(value) };
    }

    private static SchemataProcess NewProcess(string definitionName) {
        return new() {
            Name           = "p1",
            CanonicalName  = "processes/p1",
            DefinitionName = definitionName,
        };
    }
}
