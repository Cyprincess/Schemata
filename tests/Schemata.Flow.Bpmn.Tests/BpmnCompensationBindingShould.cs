using System;
using System.Linq;
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

    [Fact]
    public async Task Advance_TwoBoundariesOnTwoHosts_DrainsSameActivityInOrder_AcrossActivitiesLifo() {
        var start  = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var hostA  = new NoneTask { Name = "host-a" };
        var hostB  = new NoneTask { Name = "host-b" };
        var pause  = new NoneTask { Name = "pause" };
        var throwEvent = new FlowEvent {
            Name       = "throw",
            Position   = EventPosition.IntermediateThrow,
            Definition = new CompensationDefinition { Name = "compensate" },
        };
        var after = new NoneTask { Name = "after" };
        var end   = new FlowEvent { Name = "end", Position = EventPosition.End };
        var undoA1 = new NoneTask { Name = "undo-a1" };
        var undoA2 = new NoneTask { Name = "undo-a2" };
        var undoB1 = new NoneTask { Name = "undo-b1" };
        var undoB2 = new NoneTask { Name = "undo-b2" };
        var boundaryA1 = Boundary("boundary-a1", hostA);
        var boundaryA2 = Boundary("boundary-a2", hostA);
        var boundaryB1 = Boundary("boundary-b1", hostB);
        var boundaryB2 = Boundary("boundary-b2", hostB);
        var definition = new ProcessDefinition {
            Name = "compensation-two-hosts",
            Elements = { start, hostA, hostB, pause, throwEvent, after, end,
                         boundaryA1, boundaryA2, boundaryB1, boundaryB2, undoA1, undoA2, undoB1, undoB2 },
            Flows = {
                new() { Source = start, Target = hostA },
                new() { Source = hostA, Target = hostB },
                new() { Source = hostB, Target = pause },
                new() { Source = pause, Target = throwEvent },
                new() { Source = throwEvent, Target = after },
                new() { Source = after, Target = end },
                new() { Source = boundaryA1, Target = undoA1 },
                new() { Source = boundaryA2, Target = undoA2 },
                new() { Source = boundaryB1, Target = undoB1 },
                new() { Source = boundaryB2, Target = undoB2 },
            },
        };
        var engine  = new BpmnEngine();
        var process = Process(definition);

        var started  = await engine.StartAsync(definition, process, CancellationToken.None);
        var afterA   = await engine.AdvanceAsync(definition, started.Process, started.Tokens, null, CancellationToken.None);
        var afterB   = await engine.AdvanceAsync(definition, afterA.Process, afterA.Tokens, null, CancellationToken.None);

        Assert.Equal(
            ["boundary-a1", "boundary-a2", "boundary-b1", "boundary-b2"],
            afterB.CompensationBindings.OrderBy(binding => binding.RegistrationOrder).Select(binding => binding.BoundaryName));

        var compensated = await new BpmnEngine().AdvanceAsync(
            definition,
            afterB.Process,
            afterB.Tokens,
            BpmnEngineTestExtensions.Context(afterB.CompensationBindings),
            null,
            CancellationToken.None);

        var undos = compensated.Transitions
                               .Where(transition => transition.Kind == TransitionKind.Compensate)
                               .Select(transition => transition.Posterior)
                               .ToList();
        Assert.Equal(["undo-b1", "undo-b2", "undo-a1", "undo-a2"], undos);
    }

    private static FlowEvent Boundary(string name, Activity host) {
        return new() {
            Name       = name,
            Position   = EventPosition.Boundary,
            AttachedTo = host,
            Definition = new CompensationDefinition { Name = name, Activity = host },
        };
    }

    [Fact]
    public async Task Advance_MidGroupHandlerFailure_RemovesOnlyTheCompensatedBinding() {
        var undo1Runs = 0;
        var undo2Runs = 0;
        var start    = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var sub      = new EmbeddedSubProcess { Name = "sub" };
        var end      = new FlowEvent { Name = "end", Position = EventPosition.End };
        var errorEnd = new FlowEvent { Name = "error-end", Position = EventPosition.End };
        var undo1 = new ProcedureTask {
            Name = "undo-1",
            Body = (_, _) => {
                undo1Runs++;
                return ValueTask.CompletedTask;
            },
        };
        var undo2 = new ProcedureTask {
            Name = "undo-2",
            Body = (_, _) => {
                undo2Runs++;
                return undo2Runs == 1 ? throw new InvalidOperationException("undo-2 boom") : ValueTask.CompletedTask;
            },
        };
        var cstart = new FlowEvent { Name = "child-start", Position = EventPosition.Start };
        var host   = new NoneTask { Name = "host" };
        var pause  = new NoneTask { Name = "pause" };
        var throwEvent = new FlowEvent {
            Name       = "throw",
            Position   = EventPosition.IntermediateThrow,
            Definition = new CompensationDefinition { Name = "compensate" },
        };
        var cend = new FlowEvent { Name = "child-end", Position = EventPosition.End };
        sub.Children.Add(cstart);
        sub.Children.Add(host);
        sub.Children.Add(pause);
        sub.Children.Add(throwEvent);
        sub.Children.Add(cend);
        sub.ChildFlows.Add(new() { Source = cstart, Target = host });
        sub.ChildFlows.Add(new() { Source = host, Target = pause });
        sub.ChildFlows.Add(new() { Source = pause, Target = throwEvent });
        sub.ChildFlows.Add(new() { Source = throwEvent, Target = cend });

        var boundary1 = Boundary("boundary-a1", host);
        var boundary2 = Boundary("boundary-a2", host);
        var errorBoundary = new FlowEvent {
            Name         = "error-sub",
            Position     = EventPosition.Boundary,
            AttachedTo   = sub,
            Interrupting = true,
            Definition   = new ErrorDefinition { Name = "fault", ExceptionType = typeof(InvalidOperationException) },
        };
        var definition = new ProcessDefinition {
            Name     = "compensation-mid-group-failure",
            Elements = { start, sub, end, errorEnd, boundary1, boundary2, errorBoundary, undo1, undo2 },
            Flows = {
                new() { Source = start, Target = sub },
                new() { Source = sub, Target = end },
                new() { Source = boundary1, Target = undo1 },
                new() { Source = boundary2, Target = undo2 },
                new() { Source = errorBoundary, Target = errorEnd },
            },
        };
        var engine  = new BpmnEngine();
        var process = Process(definition);

        var started = await engine.StartAsync(definition, process, CancellationToken.None);
        var child   = started.Tokens.Single(t => t.StateName == "host");
        var atPause = await engine.AdvanceAsync(definition, started.Process, started.Tokens, child.CanonicalName, CancellationToken.None);

        Assert.Equal(
            ["boundary-a1", "boundary-a2"],
            atPause.CompensationBindings.OrderBy(binding => binding.RegistrationOrder).Select(binding => binding.BoundaryName));

        var routed = await engine.AdvanceAsync(definition, atPause.Process, atPause.Tokens, child.CanonicalName, CancellationToken.None);

        Assert.Equal(1, undo1Runs);
        Assert.Equal(1, undo2Runs);
        Assert.Contains(routed.Transitions, t => t.Kind == TransitionKind.Compensate && t.Event == "boundary-a1");
        Assert.DoesNotContain(routed.Transitions, t => t.Kind == TransitionKind.Compensate && t.Event == "boundary-a2");
        var surviving = Assert.Single(routed.CompensationBindings);
        Assert.Equal("boundary-a2", surviving.BoundaryName);
    }

    [Fact]
    public async Task Advance_TargetedCompensation_DrainsBothBoundariesInRegistrationOrder() {
        var start = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var host  = new NoneTask { Name = "host" };
        var pause = new NoneTask { Name = "pause" };
        var end   = new FlowEvent { Name = "end", Position = EventPosition.End };
        var undo1 = new NoneTask { Name = "undo-1" };
        var undo2 = new NoneTask { Name = "undo-2" };
        var boundary1 = Boundary("boundary-1", host);
        var boundary2 = Boundary("boundary-2", host);
        var throwEvent = new FlowEvent {
            Name       = "throw",
            Position   = EventPosition.IntermediateThrow,
            Definition = new CompensationDefinition { Name = "compensate", Activity = host },
        };
        var definition = new ProcessDefinition {
            Name     = "compensation-targeted-two-boundaries",
            Elements = { start, host, pause, throwEvent, end, boundary1, boundary2, undo1, undo2 },
            Flows = {
                new() { Source = start, Target = host },
                new() { Source = host, Target = pause },
                new() { Source = pause, Target = throwEvent },
                new() { Source = throwEvent, Target = end },
                new() { Source = boundary1, Target = undo1 },
                new() { Source = boundary2, Target = undo2 },
            },
        };
        var engine  = new BpmnEngine();
        var process = Process(definition);

        var started   = await engine.StartAsync(definition, process, CancellationToken.None);
        var afterHost = await engine.AdvanceAsync(definition, started.Process, started.Tokens, null, CancellationToken.None);

        var compensated = await engine.AdvanceAsync(definition, afterHost.Process, afterHost.Tokens, null, CancellationToken.None);

        var undos = compensated.Transitions
                               .Where(transition => transition.Kind == TransitionKind.Compensate)
                               .Select(transition => transition.Posterior)
                               .ToList();
        Assert.Equal(["undo-1", "undo-2"], undos);
        Assert.Empty(compensated.CompensationBindings);
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
