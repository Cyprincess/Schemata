using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Xunit;

namespace Schemata.Flow.Bpmn.Tests;

public class BpmnCompensationBindingShould
{
    [Fact]
    public async Task Advance_Registered_Boundary_Emits_Binding_And_New_Engine_Restores_It() {
        var definition = Definition();
        var engine     = new BpmnEngine();
        var process    = Process(definition);

        var started   = await engine.StartAsync(definition, process, CancellationToken.None);
        var afterHost = await engine.AdvanceAsync(definition, started.Process, started.Tokens, null, CancellationToken.None);
        var binding   = Assert.Single(afterHost.CompensationBindings);

        Assert.Equal(process.CanonicalName, binding.ScopeOwnerCanonicalName);
        Assert.Equal("host", binding.ActivityName);
        Assert.Equal(0, binding.RegistrationOrder);

        var restored = await new BpmnEngine().AdvanceAsync(
            definition,
            afterHost.Process,
            afterHost.Tokens,
            BpmnEngineTestExtensions.Context(afterHost.CompensationBindings),
            null,
            CancellationToken.None);

        Assert.Contains(restored.Transitions, transition => transition.Kind == TransitionKind.Compensate && transition.Previous == "host");
    }

    [Fact]
    public async Task Advance_Compensation_Throw_Without_Loaded_Binding_Throws_Explicit_Error() {
        var definition = Definition();
        var engine     = new BpmnEngine();
        var process    = Process(definition);
        var started    = await engine.StartAsync(definition, process, CancellationToken.None);
        var afterHost  = await engine.AdvanceAsync(definition, started.Process, started.Tokens, null, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => new BpmnEngine().AdvanceAsync(
            definition,
            afterHost.Process,
            afterHost.Tokens,
            BpmnEngineTestExtensions.Context([]),
            null,
            CancellationToken.None).AsTask());

        Assert.Contains("Compensation binding is missing", exception.Message);
    }

    private static ProcessDefinition Definition() {
        var start = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var host  = new NoneTask { Name = "host" };
        var pause = new NoneTask { Name = "pause" };
        var throwEvent = new FlowEvent {
            Name       = "throw",
            Position   = EventPosition.IntermediateThrow,
            Definition = new CompensationDefinition { Name = "compensate" },
        };
        var after  = new NoneTask { Name = "after" };
        var end    = new FlowEvent { Name = "end", Position = EventPosition.End };
        var boundary = new FlowEvent {
            Name       = "compensate-host",
            Position   = EventPosition.Boundary,
            AttachedTo = host,
            Definition = new CompensationDefinition { Name = "compensate-host", Activity = host },
        };
        var undo = new NoneTask { Name = "undo-host" };

        return new() {
            Name     = "compensation-binding",
            Elements = { start, host, pause, throwEvent, after, end, boundary, undo },
            Flows = {
                new() { Source = start, Target = host },
                new() { Source = host, Target = pause },
                new() { Source = pause, Target = throwEvent },
                new() { Source = throwEvent, Target = after },
                new() { Source = after, Target = end },
                new() { Source = boundary, Target = undo },
            },
        };
    }

    private static SchemataProcess Process(ProcessDefinition definition) {
        return new() {
            Name           = "p1",
            CanonicalName  = "processes/p1",
            DefinitionName = definition.Name,
        };
    }
}
