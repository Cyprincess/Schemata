using Schemata.Abstractions.Exceptions;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.StateMachine;
using Xunit;

namespace Schemata.Flow.Tests;

public class StateMachineValidatorTests
{
    [Fact]
    public void Validate_ValidDefinition_Passes() {
        var startEvent = new FlowEvent { Id = "start", Name = "Start", Position = EventPosition.Start };
        var endEvent = new FlowEvent { Id = "end", Name = "End", Position = EventPosition.End };
        var task = new NoneTask { Id = "task", Name = "Task" };

        var definition = new ProcessDefinition {
            Name = "test",
            Elements = { startEvent, endEvent, task },
            Flows = {
                new() { Id = "f1", Source = startEvent, Target = task },
                new() { Id = "f2", Source = task, Target = endEvent },
            },
        };

        StateMachineValidator.Validate(definition);
    }

    [Fact]
    public void Validate_MissingStartEvent_Throws() {
        var endEvent = new FlowEvent { Id = "end", Name = "End", Position = EventPosition.End };

        var definition = new ProcessDefinition {
            Name = "test",
            Elements = { endEvent },
        };

        Assert.Throws<FailedPreconditionException>(() => StateMachineValidator.Validate(definition));
    }

    [Fact]
    public void Validate_ParallelGateway_Throws() {
        var startEvent = new FlowEvent { Id = "start", Name = "Start", Position = EventPosition.Start };
        var endEvent = new FlowEvent { Id = "end", Name = "End", Position = EventPosition.End };
        var gateway = new ParallelGateway { Id = "gw", Name = "GW" };

        var definition = new ProcessDefinition {
            Name = "test",
            Elements = { startEvent, endEvent, gateway },
            Flows = { new() { Id = "f1", Source = startEvent, Target = gateway } },
        };

        Assert.Throws<FailedPreconditionException>(() => StateMachineValidator.Validate(definition));
    }

    [Fact]
    public void Validate_BoundaryEvent_Throws() {
        var startEvent = new FlowEvent { Id = "start", Name = "Start", Position = EventPosition.Start };
        var endEvent = new FlowEvent { Id = "end", Name = "End", Position = EventPosition.End };
        var task = new NoneTask { Id = "task", Name = "Task" };
        var boundaryEvent = new FlowEvent {
            Id = "be",
            Name = "BE",
            Position = EventPosition.Boundary,
            AttachedTo = task,
        };

        var definition = new ProcessDefinition {
            Name = "test",
            Elements = { startEvent, endEvent, task, boundaryEvent },
            Flows = { new() { Id = "f1", Source = startEvent, Target = endEvent } },
        };

        Assert.Throws<FailedPreconditionException>(() => StateMachineValidator.Validate(definition));
    }

    [Fact]
    public void Validate_ParallelEventBasedGateway_Throws() {
        var startEvent = new FlowEvent { Id = "start", Name = "Start", Position = EventPosition.Start };
        var gateway = new EventBasedGateway { Id = "gw", Name = "GW", Parallel = true };
        var catchEvent = new FlowEvent { Id = "catch", Name = "Catch", Position = EventPosition.IntermediateCatch };
        var endEvent = new FlowEvent { Id = "end", Name = "End", Position = EventPosition.End };

        var definition = new ProcessDefinition {
            Name = "test",
            Elements = { startEvent, gateway, catchEvent, endEvent },
            Flows = {
                new() { Id = "f1", Source = startEvent, Target = gateway },
                new() { Id = "f2", Source = gateway, Target = catchEvent },
                new() { Id = "f3", Source = catchEvent, Target = endEvent },
            },
        };

        Assert.Throws<FailedPreconditionException>(() => StateMachineValidator.Validate(definition));
    }

    [Fact]
    public void Validate_DuplicateElementNames_Throws() {
        var startEvent = new FlowEvent { Id = "start", Name = "Same", Position = EventPosition.Start };
        var endEvent = new FlowEvent { Id = "end", Name = "Same", Position = EventPosition.End };

        var definition = new ProcessDefinition {
            Name = "test",
            Elements = { startEvent, endEvent },
        };

        Assert.Throws<FailedPreconditionException>(() => StateMachineValidator.Validate(definition));
    }

    [Fact]
    public void Validate_FlowUnknownSource_Throws() {
        var startEvent = new FlowEvent { Id = "start", Name = "Start", Position = EventPosition.Start };
        var endEvent = new FlowEvent { Id = "end", Name = "End", Position = EventPosition.End };
        var unknownTask = new NoneTask { Id = "unknown", Name = "Unknown" };

        var definition = new ProcessDefinition {
            Name = "test",
            Elements = { startEvent, endEvent },
            Flows = {
                new() { Id = "f1", Source = startEvent, Target = endEvent },
                new() { Id = "f2", Source = unknownTask, Target = endEvent },
            },
        };

        Assert.Throws<FailedPreconditionException>(() => StateMachineValidator.Validate(definition));
    }

    [Fact]
    public void Validate_FlowUnknownTarget_Throws() {
        var startEvent = new FlowEvent { Id = "start", Name = "Start", Position = EventPosition.Start };
        var endEvent = new FlowEvent { Id = "end", Name = "End", Position = EventPosition.End };
        var unknownTask = new NoneTask { Id = "unknown", Name = "Unknown" };

        var definition = new ProcessDefinition {
            Name = "test",
            Elements = { startEvent, endEvent },
            Flows = {
                new() { Id = "f1", Source = startEvent, Target = endEvent },
                new() { Id = "f2", Source = startEvent, Target = unknownTask },
            },
        };

        Assert.Throws<FailedPreconditionException>(() => StateMachineValidator.Validate(definition));
    }

    [Fact]
    public void Validate_EndEventWithOutgoing_Throws() {
        var startEvent = new FlowEvent { Id = "start", Name = "Start", Position = EventPosition.Start };
        var endEvent = new FlowEvent { Id = "end", Name = "End", Position = EventPosition.End };
        var task = new NoneTask { Id = "task", Name = "Task" };

        var definition = new ProcessDefinition {
            Name = "test",
            Elements = { startEvent, endEvent, task },
            Flows = {
                new() { Id = "f1", Source = startEvent, Target = endEvent },
                new() { Id = "f2", Source = endEvent, Target = task },
            },
        };

        Assert.Throws<FailedPreconditionException>(() => StateMachineValidator.Validate(definition));
    }

    [Fact]
    public void Validate_IntermediateCatchNotFromEventBasedGateway_Throws() {
        var startEvent = new FlowEvent { Id = "start", Name = "Start", Position = EventPosition.Start };
        var catchEvent = new FlowEvent { Id = "catch", Name = "Catch", Position = EventPosition.IntermediateCatch };
        var endEvent = new FlowEvent { Id = "end", Name = "End", Position = EventPosition.End };

        var definition = new ProcessDefinition {
            Name = "test",
            Elements = { startEvent, catchEvent, endEvent },
            Flows = {
                new() { Id = "f1", Source = startEvent, Target = catchEvent },
                new() { Id = "f2", Source = catchEvent, Target = endEvent },
            },
        };

        Assert.Throws<FailedPreconditionException>(() => StateMachineValidator.Validate(definition));
    }

    [Fact]
    public void Validate_ActivityMultipleDirectOutgoing_Throws() {
        var startEvent = new FlowEvent { Id = "start", Name = "Start", Position = EventPosition.Start };
        var task = new NoneTask { Id = "task", Name = "Task" };
        var task2 = new NoneTask { Id = "task2", Name = "Task2" };
        var task3 = new NoneTask { Id = "task3", Name = "Task3" };
        var endEvent = new FlowEvent { Id = "end", Name = "End", Position = EventPosition.End };

        var definition = new ProcessDefinition {
            Name = "test",
            Elements = { startEvent, task, task2, task3, endEvent },
            Flows = {
                new() { Id = "f1", Source = startEvent, Target = task },
                new() { Id = "f2", Source = task, Target = task2 },
                new() { Id = "f3", Source = task, Target = task3 },
            },
        };

        Assert.Throws<FailedPreconditionException>(() => StateMachineValidator.Validate(definition));
    }
}
