using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Exceptions;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Xunit;

namespace Schemata.Flow.Bpmn.Tests;

public class BpmnEngineBoundaryShould
{
    [Fact]
    public async Task InterruptingBoundary_FiresAndCancelsHost() {
        var (definition, signal) = InterruptingDefinition();
        var process              = NewProcess(definition.Name);
        var engine               = new BpmnEngine();

        var started = await engine.StartAsync(definition, process, CancellationToken.None);

        var hostToken = started.Tokens.First(t => t.StateName == "task");
        Assert.Equal("Active", hostToken.State);

        var fired = await engine.TriggerAsync(definition, started.Process, started.Tokens, signal, null, hostToken.CanonicalName, CancellationToken.None);

        var cancelled = fired.Tokens.First(t => t.CanonicalName == hostToken.CanonicalName);
        Assert.Equal("Cancelled", cancelled.State);

        var routed = fired.Tokens.First(t => t.StateName == "handler");
        Assert.Equal("Active", routed.State);

        Assert.Contains(fired.Transitions, t => t.Kind == TransitionKind.Cancel);
    }

    [Fact]
    public async Task NonInterruptingBoundary_SpawnsConcurrentTokenAndLeavesHostActive() {
        var (definition, signal) = NonInterruptingDefinition();
        var process              = NewProcess(definition.Name);
        var engine               = new BpmnEngine();

        var started = await engine.StartAsync(definition, process, CancellationToken.None);
        var host    = started.Tokens.First(t => t.StateName == "task");

        var fired = await engine.TriggerAsync(definition, started.Process, started.Tokens, signal, null, host.CanonicalName, CancellationToken.None);

        var stillActiveHost = fired.Tokens.First(t => t.CanonicalName == host.CanonicalName);
        Assert.Equal("Active", stillActiveHost.State);

        var spawned = fired.Tokens.First(t => t.StateName == "side-handler");
        Assert.Equal("Active", spawned.State);

        Assert.Contains(fired.Transitions, t => t.Kind == TransitionKind.Spawn);
        Assert.DoesNotContain(fired.Transitions, t => t.Kind == TransitionKind.Cancel);
    }

    [Fact]
    public async Task ErrorBoundary_FiresOnMatchingErrorDefinition() {
        var (definition, errorDef) = ErrorBoundaryDefinition();
        var process                = NewProcess(definition.Name);
        var engine                 = new BpmnEngine();

        var started = await engine.StartAsync(definition, process, CancellationToken.None);
        var host    = started.Tokens.First(t => t.StateName == "risky");

        var fired = await engine.TriggerAsync(definition, started.Process, started.Tokens, errorDef, null, host.CanonicalName, CancellationToken.None);

        var cancelled = fired.Tokens.First(t => t.CanonicalName == host.CanonicalName);
        Assert.Equal("Cancelled", cancelled.State);

        var routed = fired.Tokens.First(t => t.StateName == "compensate");
        Assert.Equal("Active", routed.State);
    }

    [Fact]
    public async Task TriggerAsync_NoMatchingBoundary_Throws() {
        var (definition, _) = InterruptingDefinition();
        var process         = NewProcess(definition.Name);
        var engine          = new BpmnEngine();

        var started = await engine.StartAsync(definition, process, CancellationToken.None);
        var host    = started.Tokens.First(t => t.StateName == "task");
        var bogus   = new Signal { Name = "nobody-listens" };

        await Assert.ThrowsAsync<InvalidArgumentException>(async () =>
            await engine.TriggerAsync(definition, started.Process, started.Tokens, bogus, null, host.CanonicalName, CancellationToken.None));
    }

    [Fact]
    public void BpmnValidator_BoundaryEventWithoutAttachedTo_Throws() {
        var start    = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var task     = new NoneTask { Name = "task" };
        var boundary = new FlowEvent { Name = "boundary", Position = EventPosition.Boundary };
        var endEvent = new FlowEvent { Name = "end", Position = EventPosition.End };

        var definition = new ProcessDefinition {
            Name     = "unattached-boundary",
            Elements = { start, task, boundary, endEvent },
            Flows = {
                new() { Source = start, Target = task },
                new() { Source = task, Target = endEvent },
                new() { Source = boundary, Target = endEvent },
            },
        };

        Assert.Throws<FailedPreconditionException>(() => BpmnValidator.Validate(definition));
    }

    [Fact]
    public void BpmnValidator_BoundaryEventWithoutOutgoing_Throws() {
        var start    = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var task     = new NoneTask { Name = "task" };
        var boundary = new FlowEvent { Name = "boundary", Position = EventPosition.Boundary, AttachedTo = task };
        var endEvent = new FlowEvent { Name = "end", Position = EventPosition.End };

        var definition = new ProcessDefinition {
            Name     = "no-outgoing-boundary",
            Elements = { start, task, boundary, endEvent },
            Flows = {
                new() { Source = start, Target = task },
                new() { Source = task, Target = endEvent },
            },
        };

        Assert.Throws<FailedPreconditionException>(() => BpmnValidator.Validate(definition));
    }

    private static (ProcessDefinition definition, Signal signal) InterruptingDefinition() {
        var signal   = new Signal { Name = "cancel-signal" };
        var start    = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var task     = new NoneTask { Name = "task" };
        var boundary = new FlowEvent {
            Name = "boundary",
            Position     = EventPosition.Boundary,
            AttachedTo   = task,
            Interrupting = true,
            Definition   = signal,
        };
        var handler   = new NoneTask { Name = "handler" };
        var endNormal = new FlowEvent { Name = "end-ok", Position = EventPosition.End };
        var endAlt    = new FlowEvent { Name = "end-cancel", Position = EventPosition.End };

        var definition = new ProcessDefinition {
            Name     = "interrupting",
            Elements = { start, task, boundary, handler, endNormal, endAlt },
            Flows = {
                new() { Source = start, Target = task },
                new() { Source = task, Target = endNormal },
                new() { Source = boundary, Target = handler },
                new() { Source = handler, Target = endAlt },
            },
        };

        return (definition, signal);
    }

    private static (ProcessDefinition definition, Signal signal) NonInterruptingDefinition() {
        var signal   = new Signal { Name = "side-signal" };
        var start    = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var task     = new NoneTask { Name = "task" };
        var boundary = new FlowEvent {
            Name = "boundary",
            Position     = EventPosition.Boundary,
            AttachedTo   = task,
            Interrupting = false,
            Definition   = signal,
        };
        var side      = new NoneTask { Name = "side-handler" };
        var endNormal = new FlowEvent { Name = "end-ok", Position = EventPosition.End };
        var endSide   = new FlowEvent { Name = "end-side", Position = EventPosition.End };

        var definition = new ProcessDefinition {
            Name     = "non-interrupting",
            Elements = { start, task, boundary, side, endNormal, endSide },
            Flows = {
                new() { Source = start, Target = task },
                new() { Source = task, Target = endNormal },
                new() { Source = boundary, Target = side },
                new() { Source = side, Target = endSide },
            },
        };

        return (definition, signal);
    }

    private static (ProcessDefinition definition, ErrorDefinition error) ErrorBoundaryDefinition() {
        var error    = new ErrorDefinition { Name = "BizFault", ExceptionType = typeof(InvalidOperationException) };
        var start    = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var risky    = new NoneTask { Name = "risky" };
        var boundary = new FlowEvent {
            Name = "err-boundary",
            Position     = EventPosition.Boundary,
            AttachedTo   = risky,
            Interrupting = true,
            Definition   = error,
        };
        var compensate = new NoneTask { Name = "compensate" };
        var endOk      = new FlowEvent { Name = "end-ok", Position = EventPosition.End };
        var endErr     = new FlowEvent { Name = "end-err", Position = EventPosition.End };

        var definition = new ProcessDefinition {
            Name     = "error-boundary",
            Elements = { start, risky, boundary, compensate, endOk, endErr },
            Flows = {
                new() { Source = start, Target = risky },
                new() { Source = risky, Target = endOk },
                new() { Source = boundary, Target = compensate },
                new() { Source = compensate, Target = endErr },
            },
        };

        return (definition, error);
    }

    private static SchemataProcess NewProcess(string definitionName) {
        return new() {
            Name           = "p1",
            CanonicalName  = "processes/p1",
            DefinitionName = definitionName,
        };
    }
}
