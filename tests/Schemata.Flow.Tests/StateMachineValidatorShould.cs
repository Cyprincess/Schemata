using System;
using System.Threading.Tasks;
using Schemata.Abstractions.Exceptions;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;
using Schemata.Flow.StateMachine;
using Xunit;

namespace Schemata.Flow.Tests;

public class StateMachineValidatorShould
{
    [Fact]
    public void Validate_ParallelEventDefinition_Throws() {
        var start = new FlowEvent {
            Name = "start", Position = EventPosition.Start,
            Definition = new ParallelDefinition { Name = "parallel" },
        };
        var end = new FlowEvent { Name = "end", Position = EventPosition.End };
        var definition = new ProcessDefinition {
            Elements = { start, end },
            Flows = { new() { Source = start, Target = end } },
        };

        var error = Assert.Throws<InvalidOperationException>(() => StateMachineValidator.Validate(definition));
        Assert.Contains("not supported", error.Message);
    }

    [Fact]
    public void Validate_ValidDefinition_Passes() {
        var startEvent = new FlowEvent { Name = "Start", Position = EventPosition.Start };
        var endEvent   = new FlowEvent { Name = "End", Position   = EventPosition.End };
        var task       = new NoneTask { Name  = "Task" };

        var definition = new ProcessDefinition {
            Name     = "test",
            Elements = { startEvent, endEvent, task },
            Flows = {
                new() { Source = startEvent, Target = task },
                new() { Source = task, Target       = endEvent },
            },
        };

        var ex = Record.Exception(() => StateMachineValidator.Validate(definition));

        Assert.Null(ex);
    }

    [Fact]
    public void Validate_MissingStartEvent_Throws() {
        var endEvent = new FlowEvent { Name = "End", Position = EventPosition.End };

        var definition = new ProcessDefinition { Name = "test", Elements = { endEvent } };

        var ex = Assert.Throws<FailedPreconditionException>(() => StateMachineValidator.Validate(definition));
        Assert.Contains("exactly one start", ex.Message);
    }

    [Fact]
    public void Validate_UnnamedElement_Throws() {
        var startEvent = new FlowEvent { Name = "Start", Position = EventPosition.Start };
        var endEvent   = new FlowEvent { Name = "End", Position   = EventPosition.End };
        var task       = new NoneTask();

        var definition = new ProcessDefinition {
            Name     = "test",
            Elements = { startEvent, endEvent, task },
            Flows = {
                new() { Source = startEvent, Target = task },
                new() { Source = task, Target       = endEvent },
            },
        };

        var ex = Assert.Throws<FailedPreconditionException>(() => StateMachineValidator.Validate(definition));
        Assert.Contains("non-empty name", ex.Message);
    }

    [Fact]
    public void Validate_DuplicateElementName_Throws() {
        var startEvent = new FlowEvent { Name = "Start", Position = EventPosition.Start };
        var endEvent   = new FlowEvent { Name = "End", Position   = EventPosition.End };
        var task       = new NoneTask { Name  = "Task" };
        var duplicate  = new NoneTask { Name  = "Task" };

        var definition = new ProcessDefinition {
            Name     = "test",
            Elements = { startEvent, endEvent, task, duplicate },
            Flows = {
                new() { Source = startEvent, Target = task },
                new() { Source = task, Target       = duplicate },
                new() { Source = duplicate, Target  = endEvent },
            },
        };

        var ex = Assert.Throws<FailedPreconditionException>(() => StateMachineValidator.Validate(definition));
        Assert.Contains("unique", ex.Message);
    }

    [Fact]
    public void Validate_ParallelGateway_Throws() {
        var startEvent = new FlowEvent { Name       = "Start", Position = EventPosition.Start };
        var endEvent   = new FlowEvent { Name       = "End", Position   = EventPosition.End };
        var gateway    = new ParallelGateway { Name = "GW" };

        var definition = new ProcessDefinition {
            Name     = "test",
            Elements = { startEvent, endEvent, gateway },
            Flows    = { new() { Source = startEvent, Target = gateway } },
        };

        Assert.Throws<FailedPreconditionException>(() => StateMachineValidator.Validate(definition));
    }

    [Fact]
    public void Validate_BoundaryEvent_Throws() {
        var startEvent = new FlowEvent { Name = "Start", Position = EventPosition.Start };
        var endEvent   = new FlowEvent { Name = "End", Position   = EventPosition.End };
        var task       = new NoneTask { Name  = "Task" };
        var boundaryEvent = new FlowEvent {
            Name       = "BE",
            Position   = EventPosition.Boundary,
            AttachedTo = task,
        };

        var definition = new ProcessDefinition {
            Name = "test",
            Elements = {
                startEvent,
                endEvent,
                task,
                boundaryEvent,
            },
            Flows = { new() { Source = startEvent, Target = endEvent } },
        };

        Assert.Throws<FailedPreconditionException>(() => StateMachineValidator.Validate(definition));
    }

    [Fact]
    public void Validate_ParallelEventBasedGateway_Throws() {
        var startEvent = new FlowEvent { Name = "Start", Position = EventPosition.Start };
        var gateway    = new EventBasedGateway { Name = "GW", Parallel = true };
        var catchEvent = new FlowEvent { Name = "Catch", Position = EventPosition.IntermediateCatch };
        var endEvent   = new FlowEvent { Name = "End", Position = EventPosition.End };

        var definition = new ProcessDefinition {
            Name = "test",
            Elements = {
                startEvent,
                gateway,
                catchEvent,
                endEvent,
            },
            Flows = {
                new() { Source = startEvent, Target = gateway },
                new() { Source = gateway, Target    = catchEvent },
                new() { Source = catchEvent, Target = endEvent },
            },
        };

        Assert.Throws<FailedPreconditionException>(() => StateMachineValidator.Validate(definition));
    }

    [Fact]
    public void Validate_FlowUnknownSource_Throws() {
        var startEvent  = new FlowEvent { Name  = "Start", Position = EventPosition.Start };
        var endEvent    = new FlowEvent { Name  = "End", Position   = EventPosition.End };
        var unknownTask = new NoneTask { Name   = "Unknown" };

        var definition = new ProcessDefinition {
            Name     = "test",
            Elements = { startEvent, endEvent },
            Flows = {
                new() { Source = startEvent, Target  = endEvent },
                new() { Source = unknownTask, Target = endEvent },
            },
        };

        Assert.Throws<FailedPreconditionException>(() => StateMachineValidator.Validate(definition));
    }

    [Fact]
    public void Validate_FlowUnknownTarget_Throws() {
        var startEvent  = new FlowEvent { Name  = "Start", Position = EventPosition.Start };
        var endEvent    = new FlowEvent { Name  = "End", Position   = EventPosition.End };
        var unknownTask = new NoneTask { Name   = "Unknown" };

        var definition = new ProcessDefinition {
            Name     = "test",
            Elements = { startEvent, endEvent },
            Flows = {
                new() { Source = startEvent, Target = endEvent },
                new() { Source = startEvent, Target = unknownTask },
            },
        };

        Assert.Throws<FailedPreconditionException>(() => StateMachineValidator.Validate(definition));
    }

    [Fact]
    public void Validate_EndEventWithOutgoing_Throws() {
        var startEvent = new FlowEvent { Name = "Start", Position = EventPosition.Start };
        var endEvent   = new FlowEvent { Name = "End", Position   = EventPosition.End };
        var task       = new NoneTask { Name  = "Task" };

        var definition = new ProcessDefinition {
            Name     = "test",
            Elements = { startEvent, endEvent, task },
            Flows = {
                new() { Source = startEvent, Target = endEvent },
                new() { Source = endEvent, Target   = task },
            },
        };

        Assert.Throws<FailedPreconditionException>(() => StateMachineValidator.Validate(definition));
    }

    [Fact]
    public void Validate_IntermediateCatchNotFromEventBasedGateway_Throws() {
        var startEvent = new FlowEvent { Name = "Start", Position = EventPosition.Start };
        var catchEvent = new FlowEvent { Name = "Catch", Position = EventPosition.IntermediateCatch };
        var endEvent   = new FlowEvent { Name = "End", Position   = EventPosition.End };

        var definition = new ProcessDefinition {
            Name     = "test",
            Elements = { startEvent, catchEvent, endEvent },
            Flows = {
                new() { Source = startEvent, Target = catchEvent },
                new() { Source = catchEvent, Target = endEvent },
            },
        };

        Assert.Throws<FailedPreconditionException>(() => StateMachineValidator.Validate(definition));
    }

    [Fact]
    public void Validate_ExclusiveGatewayWithoutOutgoing_Throws() {
        var startEvent = new FlowEvent { Name        = "Start", Position = EventPosition.Start };
        var gateway    = new ExclusiveGateway { Name = "Gateway" };
        var endEvent   = new FlowEvent { Name        = "End", Position = EventPosition.End };

        var definition = new ProcessDefinition {
            Name     = "test",
            Elements = { startEvent, gateway, endEvent },
            Flows    = { new() { Source = startEvent, Target = gateway } },
        };

        var ex = Assert.Throws<FailedPreconditionException>(() => StateMachineValidator.Validate(definition));
        Assert.Contains("Exclusive gateway", ex.Message);
    }

    [Fact]
    public void Validate_IntermediateCatchWithoutOutgoing_Throws() {
        var startEvent = new FlowEvent { Name = "Start", Position = EventPosition.Start };
        var gateway    = new EventBasedGateway { Name = "Gateway" };
        var catchEvent = new FlowEvent { Name = "Catch", Position = EventPosition.IntermediateCatch };
        var endEvent   = new FlowEvent { Name = "End", Position = EventPosition.End };

        var definition = new ProcessDefinition {
            Name = "test",
            Elements = {
                startEvent,
                gateway,
                catchEvent,
                endEvent,
            },
            Flows = {
                new() { Source = startEvent, Target = gateway },
                new() { Source = gateway, Target    = catchEvent },
            },
        };

        var ex = Assert.Throws<FailedPreconditionException>(() => StateMachineValidator.Validate(definition));
        Assert.Contains("Intermediate catch event", ex.Message);
    }

    [Fact]
    public void Validate_ActivityMultipleDirectOutgoing_Throws() {
        var startEvent = new FlowEvent { Name = "Start", Position = EventPosition.Start };
        var task       = new NoneTask { Name  = "Task" };
        var task2      = new NoneTask { Name  = "Task2" };
        var task3      = new NoneTask { Name  = "Task3" };
        var endEvent   = new FlowEvent { Name = "End", Position = EventPosition.End };

        var definition = new ProcessDefinition {
            Name = "test",
            Elements = {
                startEvent,
                task,
                task2,
                task3,
                endEvent,
            },
            Flows = {
                new() { Source = startEvent, Target = task },
                new() { Source = task, Target       = task2 },
                new() { Source = task, Target       = task3 },
            },
        };

        Assert.Throws<FailedPreconditionException>(() => StateMachineValidator.Validate(definition));
    }

    [Fact]
    public void UnreachableElement_Rejected() {
        var startEvent = new FlowEvent { Name = "Start", Position = EventPosition.Start };
        var endEvent   = new FlowEvent { Name = "End", Position   = EventPosition.End };
        var task       = new NoneTask { Name  = "Task" };
        var orphan     = new NoneTask { Name  = "Orphan" };

        var definition = new ProcessDefinition {
            Name = "test",
            Elements = {
                startEvent,
                endEvent,
                task,
                orphan,
            },
            Flows = {
                new() { Source = startEvent, Target = task },
                new() { Source = task, Target       = endEvent },
                // The orphan has an outgoing flow but remains unreachable from the start event.
                new() { Source = orphan, Target = endEvent },
            },
        };

        var ex = Assert.Throws<FailedPreconditionException>(() => StateMachineValidator.Validate(definition));
        Assert.Contains("reachable", ex.Message);
    }

    [Fact]
    public void NoViableEdge_Rejected() {
        var startEvent = new FlowEvent { Name        = "Start", Position = EventPosition.Start };
        var endEvent   = new FlowEvent { Name        = "End", Position   = EventPosition.End };
        var gateway    = new ExclusiveGateway { Name = "GW" };
        var deadEnd    = new NoneTask { Name         = "DeadEnd" };

        var definition = new ProcessDefinition {
            Name = "test",
            Elements = {
                startEvent,
                endEvent,
                gateway,
                deadEnd,
            },
            Flows = {
                new() { Source = startEvent, Target = gateway },
                new() { Source = gateway, Target    = deadEnd },
                new() { Source = gateway, Target    = endEvent },
            },
        };

        var ex = Assert.Throws<FailedPreconditionException>(() => StateMachineValidator.Validate(definition));
        Assert.Contains("outgoing", ex.Message);
    }

    [Fact]
    public void Validate_Typed_Procedure_With_A_Payload_Free_Incoming_Path_Throws() {
        // The typed catch branch alone used to satisfy the check; the branch that reaches the task
        // straight from the start delivers no payload and must reject the graph.
        var definition = PayloadFreeBranchProcess();

        var ex = Assert.Throws<InvalidOperationException>(() => StateMachineValidator.Validate(definition));

        Assert.Contains("Typed procedure task payload mismatch", ex.Message);
    }

    [Fact]
    public void Validate_Typed_Procedure_Reached_Only_Through_A_Typed_Catch_Passes() {
        var start   = new FlowEvent { Name = "Start", Position = EventPosition.Start };
        var wait    = new UserTask { Name  = "Wait" };
        var gateway = new EventBasedGateway { Name = "Await" };
        var catchEv = new FlowEvent {
            Name       = "Submitted",
            Position   = EventPosition.IntermediateCatch,
            Definition = new Message<int> { Name = "submit" },
        };
        var handle = new ProcedureTask<int> { Name = "Handle", Body = (_, _, _) => ValueTask.CompletedTask };
        var end    = new FlowEvent { Name = "End", Position = EventPosition.End };

        var definition = new ProcessDefinition {
            Name     = "typed-happy-path",
            Elements = { start, wait, gateway, catchEv, handle, end },
            Flows = {
                new() { Source = start,    Target = wait },
                new() { Source = wait,     Target = gateway },
                new() { Source = gateway,  Target = catchEv },
                new() { Source = catchEv,  Target = handle },
                new() { Source = handle,   Target = end },
            },
        };
        definition.Messages.Add((Message)catchEv.Definition!);

        var ex = Record.Exception(() => StateMachineValidator.Validate(definition));

        Assert.Null(ex);
    }

    private static ProcessDefinition PayloadFreeBranchProcess() {
        var start   = new FlowEvent { Name = "Start", Position = EventPosition.Start };
        var decide  = new ExclusiveGateway { Name = "Decide" };
        var awaitGw = new EventBasedGateway { Name = "AwaitGw" };
        var catchEv = new FlowEvent {
            Name       = "Submitted",
            Position   = EventPosition.IntermediateCatch,
            Definition = new Message<int> { Name = "submit" },
        };
        var handle = new ProcedureTask<int> { Name = "Handle", Body = (_, _, _) => ValueTask.CompletedTask };
        var end    = new FlowEvent { Name = "End", Position = EventPosition.End };

        var definition = new ProcessDefinition {
            Name     = "payload-free-branch",
            Elements = { start, decide, awaitGw, catchEv, handle, end },
            Flows = {
                new() { Source = start, Target   = decide },
                new() { Source = decide, Target   = handle },
                new() { Source = decide, Target   = awaitGw, Condition = new LambdaConditionExpression { Lambda = _ => new(true) } },
                new() { Source = awaitGw, Target  = catchEv },
                new() { Source = catchEv, Target  = handle },
                new() { Source = handle, Target   = end },
            },
        };
        definition.Messages.Add((Message)catchEv.Definition!);
        return definition;
    }
}
