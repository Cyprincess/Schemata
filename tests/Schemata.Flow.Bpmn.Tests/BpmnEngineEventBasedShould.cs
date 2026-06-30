using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Exceptions;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Xunit;

namespace Schemata.Flow.Bpmn.Tests;

public class BpmnEngineEventBasedShould
{
    [Fact]
    public async Task Keep_Business_State_On_StateName_When_Parking_At_Event_Based_Gateway() {
        var definition = ExclusiveEventBased();
        var process    = NewProcess(definition.Name);
        var engine     = new BpmnEngine();

        var started = await engine.StartAsync(definition, process, CancellationToken.None);
        var parked  = await engine.AdvanceAsync(definition, started.Process, started.Tokens, null, CancellationToken.None);

        var token = Assert.Single(parked.Tokens);
        Assert.Equal("Waiting", token.State);
        Assert.Equal("src", token.StateName);
        Assert.Equal("eb", token.WaitingAtName);
        Assert.Equal("Waiting", parked.Process.State);
    }

    [Fact]
    public async Task TriggerAsync_MatchingSignal_RoutesTokenAlongCatch() {
        var (definition, approvedSignal, rejectedSignal) = ExclusiveEventBasedWithEvents();
        var process                                       = NewProcess(definition.Name);
        var engine                                        = new BpmnEngine();

        var started = await engine.StartAsync(definition, process, CancellationToken.None);
        var parked  = await engine.AdvanceAsync(definition, started.Process, started.Tokens, null, CancellationToken.None);
        var routed  = await engine.TriggerAsync(definition, parked.Process, parked.Tokens, approvedSignal, null, null, CancellationToken.None);

        var token = Assert.Single(routed.Tokens);
        Assert.Equal("approved-task", token.StateName);
        Assert.Equal("Active", token.State);

        Assert.DoesNotContain(routed.Tokens, t => t.StateName == "rejected-task");
        _ = rejectedSignal;
    }

    [Fact]
    public async Task TriggerAsync_UnknownTrigger_ThrowsInvalidArgument() {
        var (definition, _, _) = ExclusiveEventBasedWithEvents();
        var process            = NewProcess(definition.Name);
        var engine             = new BpmnEngine();

        var started = await engine.StartAsync(definition, process, CancellationToken.None);
        var parked  = await engine.AdvanceAsync(definition, started.Process, started.Tokens, null, CancellationToken.None);
        var bogus   = new Signal { Name = "nobody-listens" };

        await Assert.ThrowsAsync<InvalidArgumentException>(async () =>
            await engine.TriggerAsync(definition, parked.Process, parked.Tokens, bogus, null, null, CancellationToken.None));
    }

    [Fact]
    public async Task TriggerAsync_TokenNotWaiting_ThrowsInvalidArgument() {
        var (definition, approvedSignal, _) = ExclusiveEventBasedWithEvents();
        var process                          = NewProcess(definition.Name);
        var engine                           = new BpmnEngine();

        var started = await engine.StartAsync(definition, process, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidArgumentException>(async () =>
            await engine.TriggerAsync(definition, started.Process, started.Tokens, approvedSignal, null, null, CancellationToken.None));
    }

    [Fact]
    public async Task TriggerAsync_ParallelGateway_SpawnsTokenPerMatchingCatch() {
        var (definition, signal) = ParallelEventBasedDefinition();
        var process              = NewProcess(definition.Name);
        var engine               = new BpmnEngine();

        var started = await engine.StartAsync(definition, process, CancellationToken.None);
        var parked  = await engine.AdvanceAsync(definition, started.Process, started.Tokens, null, CancellationToken.None);
        var routed  = await engine.TriggerAsync(definition, parked.Process, parked.Tokens, signal, null, null, CancellationToken.None);

        var active = routed.Tokens.Where(t => t.State == "Active").Select(t => t.StateName).OrderBy(s => s).ToList();
        Assert.Equal(new[] { "branch-a", "branch-b" }, active);

        Assert.Contains(routed.Transitions, t => t.Kind == TransitionKind.Fork);
    }

    [Fact]
    public async Task Surface_Gateway_Name_When_Process_Starts_Into_Await() {
        var definition = EventBasedDirectlyAfterStart();
        var process    = NewProcess(definition.Name);
        var engine     = new BpmnEngine();

        var started = await engine.StartAsync(definition, process, CancellationToken.None);

        var token = Assert.Single(started.Tokens);
        Assert.Equal("Waiting", token.State);
        Assert.Equal("eb", token.StateName);
    }

    [Fact]
    public void BpmnValidator_EventBasedGatewayWithoutOutgoing_Throws() {
        var start    = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var gateway  = new EventBasedGateway { Name = "eb" };
        var endEvent = new FlowEvent { Name = "end", Position = EventPosition.End };
        var definition = new ProcessDefinition {
            Name     = "broken-eventbased",
            Elements = { start, gateway, endEvent },
            Flows    = { new() { Source = start, Target = gateway } },
        };

        Assert.Throws<FailedPreconditionException>(() => BpmnValidator.Validate(definition));
    }

    [Fact]
    public void BpmnValidator_EventBasedGatewayOutgoingTargetMustBeIntermediateCatch_Throws() {
        var start    = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var gateway  = new EventBasedGateway { Name = "eb" };
        var direct   = new NoneTask { Name = "task" };
        var endEvent = new FlowEvent { Name = "end", Position = EventPosition.End };
        var definition = new ProcessDefinition {
            Name     = "wrong-target-eventbased",
            Elements = { start, gateway, direct, endEvent },
            Flows = {
                new() { Source = start, Target = gateway },
                new() { Source = gateway, Target = direct },
                new() { Source = direct, Target = endEvent },
            },
        };

        Assert.Throws<FailedPreconditionException>(() => BpmnValidator.Validate(definition));
    }

    private static ProcessDefinition ExclusiveEventBased() {
        var start         = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var src           = new NoneTask { Name = "src" };
        var gateway       = new EventBasedGateway { Name = "eb" };
        var approvedCatch = new FlowEvent { Name = "approved-catch", Position = EventPosition.IntermediateCatch };
        var rejectedCatch = new FlowEvent { Name = "rejected-catch", Position = EventPosition.IntermediateCatch };
        var endApproved   = new FlowEvent { Name = "end-a", Position = EventPosition.End };
        var endRejected   = new FlowEvent { Name = "end-r", Position = EventPosition.End };

        return new() {
            Name     = "exclusive-eventbased",
            Elements = { start, src, gateway, approvedCatch, rejectedCatch, endApproved, endRejected },
            Flows = {
                new() { Source = start, Target = src },
                new() { Source = src, Target = gateway },
                new() { Source = gateway, Target = approvedCatch },
                new() { Source = gateway, Target = rejectedCatch },
                new() { Source = approvedCatch, Target = endApproved },
                new() { Source = rejectedCatch, Target = endRejected },
            },
        };
    }

    private static (ProcessDefinition definition, Signal approved, Signal rejected) ExclusiveEventBasedWithEvents() {
        var approved = new Signal { Name = "approved" };
        var rejected = new Signal { Name = "rejected" };

        var start         = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var src           = new NoneTask { Name = "src" };
        var gateway       = new EventBasedGateway { Name = "eb" };
        var approvedCatch = new FlowEvent {
            Name = "approved-catch",
            Position   = EventPosition.IntermediateCatch,
            Definition = approved,
        };
        var rejectedCatch = new FlowEvent {
            Name = "rejected-catch",
            Position   = EventPosition.IntermediateCatch,
            Definition = rejected,
        };
        var approvedTask = new NoneTask { Name = "approved-task" };
        var rejectedTask = new NoneTask { Name = "rejected-task" };
        var endApproved  = new FlowEvent { Name = "end-a", Position = EventPosition.End };
        var endRejected  = new FlowEvent { Name = "end-r", Position = EventPosition.End };

        var definition = new ProcessDefinition {
            Name     = "exclusive-eventbased",
            Elements = { start, src, gateway, approvedCatch, rejectedCatch, approvedTask, rejectedTask, endApproved, endRejected },
            Flows = {
                new() { Source = start, Target = src },
                new() { Source = src, Target = gateway },
                new() { Source = gateway, Target = approvedCatch },
                new() { Source = gateway, Target = rejectedCatch },
                new() { Source = approvedCatch, Target = approvedTask },
                new() { Source = rejectedCatch, Target = rejectedTask },
                new() { Source = approvedTask, Target = endApproved },
                new() { Source = rejectedTask, Target = endRejected },
            },
        };

        return (definition, approved, rejected);
    }

    private static (ProcessDefinition definition, Signal signal) ParallelEventBasedDefinition() {
        var signal = new Signal { Name = "broadcast" };

        var start    = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var src      = new NoneTask { Name = "src" };
        var gateway  = new EventBasedGateway { Name = "eb", Parallel = true };
        var catchA   = new FlowEvent {
            Name = "catch-a",
            Position   = EventPosition.IntermediateCatch,
            Definition = signal,
        };
        var catchB = new FlowEvent {
            Name = "catch-b",
            Position   = EventPosition.IntermediateCatch,
            Definition = signal,
        };
        var branchA = new NoneTask { Name = "branch-a" };
        var branchB = new NoneTask { Name = "branch-b" };
        var endA    = new FlowEvent { Name = "end-a", Position = EventPosition.End };
        var endB    = new FlowEvent { Name = "end-b", Position = EventPosition.End };

        var definition = new ProcessDefinition {
            Name     = "parallel-eventbased",
            Elements = { start, src, gateway, catchA, catchB, branchA, branchB, endA, endB },
            Flows = {
                new() { Source = start, Target = src },
                new() { Source = src, Target = gateway },
                new() { Source = gateway, Target = catchA },
                new() { Source = gateway, Target = catchB },
                new() { Source = catchA, Target = branchA },
                new() { Source = catchB, Target = branchB },
                new() { Source = branchA, Target = endA },
                new() { Source = branchB, Target = endB },
            },
        };

        return (definition, signal);
    }

    private static ProcessDefinition EventBasedDirectlyAfterStart() {
        var start    = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var gateway  = new EventBasedGateway { Name = "eb" };
        var catchA   = new FlowEvent { Name = "catch-a", Position = EventPosition.IntermediateCatch };
        var catchB   = new FlowEvent { Name = "catch-b", Position = EventPosition.IntermediateCatch };
        var endA     = new FlowEvent { Name = "end-a", Position = EventPosition.End };
        var endB     = new FlowEvent { Name = "end-b", Position = EventPosition.End };

        return new() {
            Name     = "eventbased-after-start",
            Elements = { start, gateway, catchA, catchB, endA, endB },
            Flows = {
                new() { Source = start, Target = gateway },
                new() { Source = gateway, Target = catchA },
                new() { Source = gateway, Target = catchB },
                new() { Source = catchA, Target = endA },
                new() { Source = catchB, Target = endB },
            },
        };
    }

    private static SchemataProcess NewProcess(string definitionName) {
        return new() {
            Name           = "p1",
            CanonicalName  = "processes/p1",
            DefinitionName = definitionName,
        };
    }
}
