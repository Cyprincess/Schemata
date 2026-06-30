using System.Linq;
using System.Threading.Tasks;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
using Schemata.Flow.Skeleton.Builders;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;
using Xunit;

namespace Schemata.Flow.Bpmn.Tests;

public class BpmnValidatorShould
{
    [Fact]
    public void Validate_LinearDefinition_Passes() {
        var start    = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var task     = new NoneTask { Name = "task" };
        var endEvent = new FlowEvent { Name = "end", Position = EventPosition.End };

        var definition = new ProcessDefinition {
            Name     = "linear",
            Elements = { start, task, endEvent },
            Flows = {
                new() { Source = start, Target = task },
                new() { Source = task, Target = endEvent },
            },
        };

        var ex = Record.Exception(() => BpmnValidator.Validate(definition));
        Assert.Null(ex);
    }

    [Fact]
    public void Validate_NoStartEvent_Throws() {
        var task     = new NoneTask { Name = "task" };
        var endEvent = new FlowEvent { Name = "end", Position = EventPosition.End };

        var definition = new ProcessDefinition {
            Name     = "no-start",
            Elements = { task, endEvent },
            Flows    = { new() { Source = task, Target = endEvent } },
        };

        Assert.Throws<FailedPreconditionException>(() => BpmnValidator.Validate(definition));
    }

    [Fact]
    public void Validate_MultipleStartEvents_Throws() {
        var startA   = new FlowEvent { Name = "start-a", Position = EventPosition.Start };
        var startB   = new FlowEvent { Name = "start-b", Position = EventPosition.Start };
        var task     = new NoneTask { Name = "task" };
        var endEvent = new FlowEvent { Name = "end", Position = EventPosition.End };

        var definition = new ProcessDefinition {
            Name     = "multi-start",
            Elements = { startA, startB, task, endEvent },
            Flows = {
                new() { Source = startA, Target = task },
                new() { Source = startB, Target = task },
                new() { Source = task, Target = endEvent },
            },
        };

        Assert.Throws<FailedPreconditionException>(() => BpmnValidator.Validate(definition));
    }

    [Fact]
    public void Validate_NoEndEvent_Throws() {
        var start = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var task  = new NoneTask { Name = "task" };

        var definition = new ProcessDefinition {
            Name     = "no-end",
            Elements = { start, task },
            Flows    = { new() { Source = start, Target = task } },
        };

        Assert.Throws<FailedPreconditionException>(() => BpmnValidator.Validate(definition));
    }

    [Fact]
    public void Validate_EndEventWithOutgoing_Throws() {
        var start    = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var endEvent = new FlowEvent { Name = "end", Position = EventPosition.End };
        var task     = new NoneTask { Name = "task" };

        var definition = new ProcessDefinition {
            Name     = "end-with-outgoing",
            Elements = { start, endEvent, task },
            Flows = {
                new() { Source = start, Target = endEvent },
                new() { Source = endEvent, Target = task },
            },
        };

        Assert.Throws<FailedPreconditionException>(() => BpmnValidator.Validate(definition));
    }

    [Fact]
    public void Validate_FlowWithoutSource_Throws() {
        var start    = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var endEvent = new FlowEvent { Name = "end", Position = EventPosition.End };

        var definition = new ProcessDefinition {
            Name     = "missing-source",
            Elements = { start, endEvent },
            Flows = {
                new() { Source = start, Target = endEvent },
                new() { Source = null!, Target = endEvent },
            },
        };

        Assert.Throws<FailedPreconditionException>(() => BpmnValidator.Validate(definition));
    }

    [Fact]
    public void Validate_FlowWithUnknownTarget_Throws() {
        var start       = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var endEvent    = new FlowEvent { Name = "end", Position = EventPosition.End };
        var ghost       = new NoneTask { Name = "ghost" };

        var definition = new ProcessDefinition {
            Name     = "unknown-target",
            Elements = { start, endEvent },
            Flows = {
                new() { Source = start, Target = endEvent },
                new() { Source = start, Target = ghost },
            },
        };

        Assert.Throws<FailedPreconditionException>(() => BpmnValidator.Validate(definition));
    }

    [Fact]
    public void Validate_UnreachableElement_Throws() {
        var start    = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var task     = new NoneTask { Name = "task" };
        var orphan   = new NoneTask { Name = "orphan" };
        var endEvent = new FlowEvent { Name = "end", Position = EventPosition.End };

        var definition = new ProcessDefinition {
            Name     = "orphan",
            Elements = { start, task, orphan, endEvent },
            Flows = {
                new() { Source = start, Target = task },
                new() { Source = task, Target = endEvent },
                new() { Source = orphan, Target = endEvent },
            },
        };

        Assert.Throws<FailedPreconditionException>(() => BpmnValidator.Validate(definition));
    }

    [Fact]
    public void Validate_ParallelGatewayValid_Passes() {
        var start = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var fork  = new ParallelGateway { Name = "fork" };
        var a     = new NoneTask { Name = "a" };
        var b     = new NoneTask { Name = "b" };
        var endA  = new FlowEvent { Name = "end-a", Position = EventPosition.End };
        var endB  = new FlowEvent { Name = "end-b", Position = EventPosition.End };

        var definition = new ProcessDefinition {
            Name     = "parallel",
            Elements = { start, fork, a, b, endA, endB },
            Flows = {
                new() { Source = start, Target = fork },
                new() { Source = fork, Target = a },
                new() { Source = fork, Target = b },
                new() { Source = a, Target = endA },
                new() { Source = b, Target = endB },
            },
        };

        var ex = Record.Exception(() => BpmnValidator.Validate(definition));
        Assert.Null(ex);
    }

    [Fact]
    public void Validate_SubProcessValid_Passes() {
        var start      = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var sub        = new EmbeddedSubProcess { Name = "sub" };
        var innerStart = new FlowEvent { Name = "in-start", Position = EventPosition.Start };
        var innerEnd   = new FlowEvent { Name = "in-end", Position = EventPosition.End };
        var endEvent   = new FlowEvent { Name = "end", Position = EventPosition.End };

        sub.Children.Add(innerStart);
        sub.Children.Add(innerEnd);
        sub.ChildFlows.Add(new() { Source = innerStart, Target = innerEnd });

        var definition = new ProcessDefinition {
            Name     = "subprocess",
            Elements = { start, sub, endEvent },
            Flows = {
                new() { Source = start, Target = sub },
                new() { Source = sub, Target = endEvent },
            },
        };

        var ex = Record.Exception(() => BpmnValidator.Validate(definition));
        Assert.Null(ex);
    }

    [Fact]
    public void Validate_SubProcessWithoutInnerStart_Throws() {
        var start    = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var sub      = new EmbeddedSubProcess { Name = "sub" };
        var innerEnd = new FlowEvent { Name = "in-end", Position = EventPosition.End };
        var endEvent = new FlowEvent { Name = "end", Position = EventPosition.End };

        sub.Children.Add(innerEnd);

        var definition = new ProcessDefinition {
            Name     = "subprocess-no-start",
            Elements = { start, sub, endEvent },
            Flows = {
                new() { Source = start, Target = sub },
                new() { Source = sub, Target = endEvent },
            },
        };

        Assert.Throws<FailedPreconditionException>(() => BpmnValidator.Validate(definition));
    }

    [Fact]
    public void Validate_BoundaryEventValid_Passes() {
        var start    = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var task     = new NoneTask { Name = "task" };
        var boundary = new FlowEvent {
            Name = "boundary",
            Position     = EventPosition.Boundary,
            AttachedTo   = task,
            Interrupting = true,
        };
        var handler   = new NoneTask { Name = "handler" };
        var endNormal = new FlowEvent { Name = "end-ok", Position = EventPosition.End };
        var endAlt    = new FlowEvent { Name = "end-cancel", Position = EventPosition.End };

        var definition = new ProcessDefinition {
            Name     = "boundary-valid",
            Elements = { start, task, boundary, handler, endNormal, endAlt },
            Flows = {
                new() { Source = start, Target = task },
                new() { Source = task, Target = endNormal },
                new() { Source = boundary, Target = handler },
                new() { Source = handler, Target = endAlt },
            },
        };

        var ex = Record.Exception(() => BpmnValidator.Validate(definition));
        Assert.Null(ex);
    }

    [Fact]
    public void Validate_EventBasedGatewayValid_Passes() {
        var start    = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var gateway  = new EventBasedGateway { Name = "eb" };
        var catchA   = new FlowEvent { Name = "catch-a", Position = EventPosition.IntermediateCatch };
        var endEvent = new FlowEvent { Name = "end", Position = EventPosition.End };

        var definition = new ProcessDefinition {
            Name     = "eventbased-valid",
            Elements = { start, gateway, catchA, endEvent },
            Flows = {
                new() { Source = start, Target = gateway },
                new() { Source = gateway, Target = catchA },
                new() { Source = catchA, Target = endEvent },
            },
        };

        var ex = Record.Exception(() => BpmnValidator.Validate(definition));
        Assert.Null(ex);
    }

    [Fact]
    public void Validate_MultiInstanceWithoutLoopCardinality_Throws() {
        var task = new NoneTask {
            Name                  = "task",
            LoopCharacteristics = new MultiInstanceLoopCharacteristics(),
        };

        var definition = LinearDefinition(task);

        Assert.Throws<FailedPreconditionException>(() => BpmnValidator.Validate(definition));
    }

    [Fact]
    public void Validate_MultiInstanceWithEventBehaviorOne_Throws() {
        var task = MultiInstanceTask(MIEventBehavior.One);

        var definition = LinearDefinition(task);

        Assert.Throws<FailedPreconditionException>(() => BpmnValidator.Validate(definition));
    }

    [Fact]
    public void Validate_MultiInstanceWithEventBehaviorComplex_Throws() {
        var task = MultiInstanceTask(MIEventBehavior.Complex);

        var definition = LinearDefinition(task);

        Assert.Throws<FailedPreconditionException>(() => BpmnValidator.Validate(definition));
    }

    [Fact]
    public void Validate_MultiInstanceWithValidConfig_DoesNotThrow() {
        var task = MultiInstanceTask(MIEventBehavior.All);

        var definition = LinearDefinition(task);

        var ex = Record.Exception(() => BpmnValidator.Validate(definition));

        Assert.Null(ex);
    }

    [Fact]
    public void Validate_CallActivityWithoutCalledElement_Throws() {
        var call = new CallActivity { Name = "call", CalledElement = string.Empty };

        var definition = LinearDefinition(call);

        Assert.Throws<FailedPreconditionException>(() => BpmnValidator.Validate(definition));
    }

    [Fact]
    public void Validate_CallActivityWithCalledElementSet_DoesNotThrow() {
        var call = new CallActivity { Name = "call", CalledElement = "called" };

        var definition = LinearDefinition(call);
        var ex = Record.Exception(() => BpmnValidator.Validate(definition));

        Assert.Null(ex);
    }

    [Fact]
    public void Validate_EventSubProcessWithZeroStartEvents_Throws() {
        var eventSub = EventSubProcess();
        eventSub.Children.Add(new FlowEvent { Name = "event-end", Position = EventPosition.End });

        var definition = DefinitionWithEventSubProcess(eventSub);

        Assert.Throws<FailedPreconditionException>(() => BpmnValidator.Validate(definition));
    }

    [Fact]
    public void Validate_EventSubProcessWithMultipleStartEvents_Throws() {
        var eventSub = EventSubProcess();
        eventSub.Children.Add(new FlowEvent { Name = "event-start-a", Position = EventPosition.Start, Definition = new Message { Name = "a" } });
        eventSub.Children.Add(new FlowEvent { Name = "event-start-b", Position = EventPosition.Start, Definition = new Message { Name = "b" } });
        eventSub.Children.Add(new FlowEvent { Name = "event-end", Position = EventPosition.End });

        var definition = DefinitionWithEventSubProcess(eventSub);

        Assert.Throws<FailedPreconditionException>(() => BpmnValidator.Validate(definition));
    }

    [Fact]
    public void Validate_EventSubProcessWithStartEventMissingDefinition_Throws() {
        var eventSub = EventSubProcess();
        eventSub.Children.Add(new FlowEvent { Name = "event-start", Position = EventPosition.Start });
        eventSub.Children.Add(new FlowEvent { Name = "event-end", Position = EventPosition.End });

        var definition = DefinitionWithEventSubProcess(eventSub);

        Assert.Throws<FailedPreconditionException>(() => BpmnValidator.Validate(definition));
    }

    [Fact]
    public void Validate_EventSubProcessWithOneTriggeredStartEvent_DoesNotThrow() {
        var eventSub = EventSubProcess();
        eventSub.Children.Add(new FlowEvent { Name = "event-start", Position = EventPosition.Start, Definition = new Message { Name = "EventMessage" } });
        eventSub.Children.Add(new FlowEvent { Name = "event-end", Position = EventPosition.End });

        var definition = DefinitionWithEventSubProcess(eventSub);
        var ex = Record.Exception(() => BpmnValidator.Validate(definition));

        Assert.Null(ex);
    }

    [Fact]
    public void Validate_EscalationDefinitionWithEmptyName_Throws() {
        var escalation = new FlowEvent {
            Name         = "throw",
            Position   = EventPosition.IntermediateThrow,
            Definition = new EscalationDefinition { Name = string.Empty },
        };

        var definition = LinearDefinition(escalation);

        Assert.Throws<FailedPreconditionException>(() => BpmnValidator.Validate(definition));
    }

    [Fact]
    public void Validate_EscalationDefinitionWithName_DoesNotThrow() {
        var escalation = new FlowEvent {
            Name         = "throw",
            Position   = EventPosition.IntermediateThrow,
            Definition = new EscalationDefinition { Name = "OrderEscalation" },
        };

        var definition = LinearDefinition(escalation);
        var ex = Record.Exception(() => BpmnValidator.Validate(definition));

        Assert.Null(ex);
    }

    [Fact]
    public void Validate_TransactionSubProcessWithoutEndEvent_Throws() {
        var transaction = TransactionSubProcess();
        transaction.Children.Add(new FlowEvent { Name = "tx-start", Position = EventPosition.Start });
        transaction.Children.Add(new NoneTask { Name = "tx-task" });
        transaction.ChildFlows.Add(new() { Source = transaction.Children[0], Target = transaction.Children[1] });

        var definition = LinearDefinition(transaction);

        var ex = Assert.Throws<FailedPreconditionException>(() => BpmnValidator.Validate(definition));
        AssertReason("STATE_MACHINE_TRANSACTION_REQUIRES_END_EVENT", ex);
    }

    [Fact]
    public void Validate_TransactionSubProcessWithEndEvent_DoesNotThrow() {
        var transaction = ValidTransactionSubProcess();
        var definition  = LinearDefinition(transaction);

        var ex = Record.Exception(() => BpmnValidator.Validate(definition));

        Assert.Null(ex);
    }

    [Fact]
    public void Validate_CancelBoundaryAttachedToNonTransactionActivity_Throws() {
        var task = new NoneTask { Name = "task" };
        var definition = LinearDefinition(task);
        var boundary = new FlowEvent {
            Name         = "cancel-boundary",
            Position   = EventPosition.Boundary,
            Definition = new CancelDefinition { Name = "Cancel" },
            AttachedTo = task,
        };
        var handler   = new NoneTask { Name = "cancel-handler" };
        var cancelEnd = new FlowEvent { Name = "cancel-end", Position = EventPosition.End };

        definition.Elements.Add(boundary);
        definition.Elements.Add(handler);
        definition.Elements.Add(cancelEnd);
        definition.Flows.Add(new() { Source = boundary, Target = handler });
        definition.Flows.Add(new() { Source = handler, Target = cancelEnd });

        var ex = Assert.Throws<FailedPreconditionException>(() => BpmnValidator.Validate(definition));
        AssertReason("STATE_MACHINE_CANCEL_BOUNDARY_REQUIRES_TRANSACTION", ex);
    }

    [Fact]
    public void Validate_CancelBoundaryAttachedToTransactionSubProcess_DoesNotThrow() {
        var transaction = ValidTransactionSubProcess();
        var definition  = LinearDefinition(transaction);
        var boundary = new FlowEvent {
            Name         = "cancel-boundary",
            Position   = EventPosition.Boundary,
            Definition = new CancelDefinition { Name = "Cancel" },
            AttachedTo = transaction,
        };
        var handler   = new NoneTask { Name = "cancel-handler" };
        var cancelEnd = new FlowEvent { Name = "cancel-end", Position = EventPosition.End };

        definition.Elements.Add(boundary);
        definition.Elements.Add(handler);
        definition.Elements.Add(cancelEnd);
        definition.Flows.Add(new() { Source = boundary, Target = handler });
        definition.Flows.Add(new() { Source = handler, Target = cancelEnd });

        var ex = Record.Exception(() => BpmnValidator.Validate(definition));

        Assert.Null(ex);
    }

    [Fact]
    public void Validate_FlowBypassingEnterTask_Throws() {
        var definition = new EnteredProcess();
        var catchEvent = definition.Elements.OfType<FlowEvent>()
                                   .Single(e => e.Position == EventPosition.IntermediateCatch);
        definition.Flows.Add(new() { Source = catchEvent, Target = definition.Target });

        var ex = Assert.Throws<FailedPreconditionException>(() => BpmnValidator.Validate(definition));
        AssertReason("STATE_MACHINE_ENTER_TASK_BYPASSED", ex);
    }

    private sealed class EnteredProcess : ProcessDefinition
    {
        public EnteredProcess() {
            this.During(Target).OnEnter(_ => ValueTask.CompletedTask).End();
            this.During(Waiting).Await(this.On(Go).Go(Target));
            this.Start().Go(Waiting);
        }

        public UserTask Waiting { get; } = null!;

        public UserTask Target { get; } = null!;

        public Message Go { get; } = null!;
    }

    private static TransactionSubProcess TransactionSubProcess() {
        return new() { Name = "tx" };
    }

    private static TransactionSubProcess ValidTransactionSubProcess() {
        var transaction = TransactionSubProcess();
        var start       = new FlowEvent { Name = "tx-start", Position = EventPosition.Start };
        var task        = new NoneTask { Name = "tx-task" };
        var endEvent    = new FlowEvent { Name = "tx-end", Position = EventPosition.End };

        transaction.Children.Add(start);
        transaction.Children.Add(task);
        transaction.Children.Add(endEvent);
        transaction.ChildFlows.Add(new() { Source = start, Target = task });
        transaction.ChildFlows.Add(new() { Source = task, Target = endEvent });

        return transaction;
    }

    private static void AssertReason(string reason, FailedPreconditionException exception) {
        Assert.Equal(reason, exception.Details?.OfType<ErrorInfoDetail>().Single().Reason);
    }

    private static NoneTask MultiInstanceTask(MIEventBehavior behavior) {
        return new() {
            Name                  = "task",
            LoopCharacteristics = new MultiInstanceLoopCharacteristics {
                LoopCardinality           = Cardinality(),
                OneCompletedEventBehavior = behavior,
            },
        };
    }

    private static LambdaConditionExpression Cardinality() {
        return new() { Lambda = _ => new(true) };
    }

    private static EventSubProcess EventSubProcess() {
        return new() { Name = "event-sub" };
    }

    private static ProcessDefinition LinearDefinition(FlowElement element) {
        var start    = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var endEvent = new FlowEvent { Name = "end", Position = EventPosition.End };

        return new() {
            Name     = "linear",
            Elements = { start, element, endEvent },
            Flows = {
                new() { Source = start, Target = element },
                new() { Source = element, Target = endEvent },
            },
        };
    }

    private static ProcessDefinition DefinitionWithEventSubProcess(EventSubProcess eventSub) {
        var start      = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var sub        = new EmbeddedSubProcess { Name = "sub" };
        var innerStart = new FlowEvent { Name = "inner-start", Position = EventPosition.Start };
        var innerEnd   = new FlowEvent { Name = "inner-end", Position = EventPosition.End };
        var endEvent   = new FlowEvent { Name = "end", Position = EventPosition.End };

        sub.Children.Add(innerStart);
        sub.Children.Add(innerEnd);
        sub.Children.Add(eventSub);
        sub.ChildFlows.Add(new() { Source = innerStart, Target = innerEnd });

        return new() {
            Name     = "event-subprocess",
            Elements = { start, sub, endEvent },
            Flows = {
                new() { Source = start, Target = sub },
                new() { Source = sub, Target = endEvent },
            },
        };
    }
}
