using System;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Abstractions.Exceptions;
using Schemata.Expressions.Aip;
using Schemata.Expressions.Skeleton;
using Schemata.Flow.Foundation;
using Schemata.Flow.Skeleton;
using Schemata.Flow.Skeleton.Builders;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;
using Schemata.Flow.StateMachine;
using Xunit;
using SystemTask = System.Threading.Tasks.Task;

namespace Schemata.Flow.Tests;

public class StateMachineEngineShould
{
    [Fact]
    public async SystemTask StartAsync_WithValidDefinition_ReturnsInitialState() {
        var engine     = new StateMachineEngine();
        var definition = CreateSimpleDefinition();
        var process    = new SchemataProcess();

        var instance = await engine.StartAsync(definition, process);

        Assert.Equal("Draft", instance.State);
        Assert.False(instance.IsComplete);
    }

    [Fact]
    public async SystemTask AdvanceAsync_ValidTransition_ChangesState() {
        var engine     = new StateMachineEngine();
        var definition = CreateSimpleDefinition();
        var process    = new SchemataProcess { State = "Draft" };

        var instance = await engine.AdvanceAsync(definition, process);

        Assert.Equal("Review", instance.State);
        Assert.False(instance.IsComplete);
    }

    [Fact]
    public async SystemTask AdvanceAsync_ToEndState_MarksComplete() {
        var engine     = new StateMachineEngine();
        var definition = CreateSimpleDefinition();
        var process    = new SchemataProcess { State = "Review" };

        var instance = await engine.AdvanceAsync(definition, process);

        Assert.Equal("Approved", instance.State);
        Assert.True(instance.IsComplete);
    }

    [Fact]
    public async SystemTask AdvanceAsync_NoOutgoingFlow_StaysAtState() {
        var engine     = new StateMachineEngine();
        var definition = CreateSimpleDefinition();
        var process    = new SchemataProcess { State = "Approved" };

        var instance = await engine.AdvanceAsync(definition, process);

        Assert.Equal("Approved", instance.State);
        Assert.False(instance.IsComplete);
    }

    [Fact]
    public async SystemTask CyclicGraph_NoStackOverflow() {
        var start = new FlowEvent { Id        = "start", Name = "Start", Position = EventPosition.Start };
        var gw1   = new ExclusiveGateway { Id = "gw1", Name   = "GW1" };
        var gw2   = new ExclusiveGateway { Id = "gw2", Name   = "GW2" };

        var definition = new ProcessDefinition {
            Name     = "cyclic",
            Elements = { start, gw1, gw2 },
            Flows = {
                new() { Id = "f1", Source = start, Target = gw1 },
                new() { Id = "f2", Source = gw1, Target   = gw2 },
                new() { Id = "f3", Source = gw2, Target   = gw1 },
            },
        };

        var engine = new StateMachineEngine();

        // A pure gateway cycle would recurse forever; the engine fails fast on revisit.
        await Assert.ThrowsAsync<FailedPreconditionException>(() => engine.StartAsync(definition, new()).AsTask());
    }

    [Fact]
    public async SystemTask AdvanceAsync_ConditionalCondition_MatchesWhenTrue() {
        var engine     = new StateMachineEngine();
        var definition = CreateConditionalDefinition();
        var process    = new SchemataProcess { State = "Draft", Variables = "{\"amount\":100}" };

        var instance = await engine.AdvanceAsync(definition, process);

        Assert.Equal("Review", instance.State);
        Assert.False(instance.IsComplete);
    }

    [Fact]
    public async SystemTask AdvanceAsync_ConditionalCondition_TakesOtherwisePath() {
        var engine     = new StateMachineEngine();
        var definition = CreateConditionalDefinition();
        var process    = new SchemataProcess { State = "Draft", Variables = "{\"amount\":10}" };

        var instance = await engine.AdvanceAsync(definition, process);

        Assert.Equal("Rejected", instance.State);
        Assert.True(instance.IsComplete);
    }

    private static ProcessDefinition CreateConditionalDefinition() {
        var startEvent     = new FlowEvent { Id = "start", Name    = "Start", Position = EventPosition.Start };
        var draftActivity  = new NoneTask { Id  = "draft", Name    = "Draft" };
        var reviewActivity = new NoneTask { Id  = "review", Name   = "Review" };
        var rejectedEvent  = new FlowEvent { Id = "rejected", Name = "Rejected", Position = EventPosition.End };
        var approvedEvent  = new FlowEvent { Id = "approved", Name = "Approved", Position = EventPosition.End };

        var gateway = new ExclusiveGateway { Id = "gateway", Name = "Decision" };

        return new() {
            Name = "conditional-approval",
            Elements = {
                startEvent,
                rejectedEvent,
                approvedEvent,
                draftActivity,
                reviewActivity,
                gateway,
            },
            Flows = {
                new() { Id = "f1", Source = startEvent, Target    = draftActivity },
                new() { Id = "f2", Source = draftActivity, Target = gateway },
                new() {
                    Id     = "f3",
                    Source = gateway,
                    Target = reviewActivity,
                    Condition = new LambdaConditionExpression {
                        Lambda = ctx => new(
                            ctx.Variables.TryGetValue("amount", out var v)
                         && v is JsonElement je
                         && je.GetInt64() > 50),
                    },
                },
                new() {
                    Id = "f4", Source = gateway, Target = rejectedEvent,
                },
                new() { Id = "f5", Source = reviewActivity, Target = approvedEvent },
            },
        };
    }

    [Fact]
    public async SystemTask AdvanceAsync_ExpressionCondition_MatchesWhenTrue() {
        var engine     = new StateMachineEngine(BuildExpressionServices());
        var definition = CreateExpressionConditionalDefinition();
        var process    = new SchemataProcess { State = "Draft", Variables = "{\"o\":{\"amount\":10}}" };

        var instance = await engine.AdvanceAsync(definition, process);

        Assert.Equal("Review", instance.State);
        Assert.False(instance.IsComplete);
    }

    [Fact]
    public async SystemTask AdvanceAsync_ExpressionCondition_TakesOtherwisePath() {
        var engine     = new StateMachineEngine(BuildExpressionServices());
        var definition = CreateExpressionConditionalDefinition();
        var process    = new SchemataProcess { State = "Draft", Variables = "{\"o\":{\"amount\":3}}" };

        var instance = await engine.AdvanceAsync(definition, process);

        Assert.Equal("Rejected", instance.State);
        Assert.True(instance.IsComplete);
    }

    private static IServiceProvider BuildExpressionServices() {
        var services = new ServiceCollection();
        services.AddAipExpressions();

        var options = new SchemataFlowOptions();
        options.Expressions.Enable(ExpressionLanguages.Aip);
        services.AddSingleton(Options.Create(options));

        return services.BuildServiceProvider();
    }

    private static ProcessDefinition CreateExpressionConditionalDefinition() {
        var startEvent     = new FlowEvent { Id = "start", Name    = "Start", Position = EventPosition.Start };
        var draftActivity  = new NoneTask { Id  = "draft", Name    = "Draft" };
        var reviewActivity = new NoneTask { Id  = "review", Name   = "Review" };
        var rejectedEvent  = new FlowEvent { Id = "rejected", Name = "Rejected", Position = EventPosition.End };
        var approvedEvent  = new FlowEvent { Id = "approved", Name = "Approved", Position = EventPosition.End };

        var gateway = new ExclusiveGateway { Id = "gateway", Name = "Decision" };

        return new() {
            Name = "expression-approval",
            Elements = {
                startEvent,
                rejectedEvent,
                approvedEvent,
                draftActivity,
                reviewActivity,
                gateway,
            },
            Flows = {
                new() { Id = "f1", Source = startEvent, Target    = draftActivity },
                new() { Id = "f2", Source = draftActivity, Target = gateway },
                new() {
                    Id        = "f3",
                    Source    = gateway,
                    Target    = reviewActivity,
                    Condition = new ExpressionConditionExpression("o.amount > 5"),
                },
                new() { Id = "f4", Source = gateway, Target        = rejectedEvent },
                new() { Id = "f5", Source = reviewActivity, Target = approvedEvent },
            },
        };
    }

    private static ProcessDefinition CreateSimpleDefinition() {
        var startEvent     = new FlowEvent { Id = "start", Name    = "Start", Position = EventPosition.Start };
        var draftActivity  = new NoneTask { Id  = "draft", Name    = "Draft" };
        var reviewActivity = new NoneTask { Id  = "review", Name   = "Review" };
        var approvedEvent  = new FlowEvent { Id = "approved", Name = "Approved", Position = EventPosition.End };

        return new() {
            Name = "simple-approval",
            Elements = {
                startEvent,
                approvedEvent,
                draftActivity,
                reviewActivity,
            },
            Flows = {
                new() { Id = "f1", Source = startEvent, Target     = draftActivity },
                new() { Id = "f2", Source = draftActivity, Target  = reviewActivity },
                new() { Id = "f3", Source = reviewActivity, Target = approvedEvent },
            },
        };
    }

    [Fact]
    public async SystemTask Dsl_BuildsValidDefinition_StartAndAdvanceWork() {
        var definition = new DslApprovalProcess();
        var engine     = new StateMachineEngine();
        var process    = new SchemataProcess();

        var instance = await engine.StartAsync(definition, process);

        Assert.Equal("Draft", instance.State);
        Assert.False(instance.IsComplete);

        process.State = instance.State;
        instance      = await engine.AdvanceAsync(definition, process);

        Assert.Equal("Review", instance.State);
        Assert.False(instance.IsComplete);

        process.State = instance.State;
        instance      = await engine.AdvanceAsync(definition, process);

        Assert.Equal("Approved", instance.State);
        Assert.True(instance.IsComplete);
    }

    [Fact]
    public async SystemTask Dsl_ConditionalBranch_WhenTrue_TakesCorrectPath() {
        var definition = new DslConditionalProcess();
        var engine     = new StateMachineEngine();
        var process    = new SchemataProcess { Variables = "{\"amount\":100}" };

        var instance = await engine.StartAsync(definition, process);
        process.State = instance.State;

        instance = await engine.AdvanceAsync(definition, process);

        Assert.Equal("Approved", instance.State);
        Assert.False(instance.IsComplete);

        process.State = instance.State;
        instance      = await engine.AdvanceAsync(definition, process);

        Assert.True(instance.IsComplete);
    }

    [Fact]
    public async SystemTask Dsl_ConditionalBranch_WhenFalse_TakesOtherwisePath() {
        var definition = new DslConditionalProcess();
        var engine     = new StateMachineEngine();
        var process    = new SchemataProcess { Variables = "{\"amount\":10}" };

        var instance = await engine.StartAsync(definition, process);
        process.State = instance.State;

        instance = await engine.AdvanceAsync(definition, process);

        Assert.Equal("Rejected", instance.State);
        Assert.False(instance.IsComplete);

        process.State = instance.State;
        instance      = await engine.AdvanceAsync(definition, process);

        Assert.True(instance.IsComplete);
    }

    [Fact]
    public async SystemTask EventBasedGateway_AwaitOnMessage_MatchesAndAdvances() {
        var definition = new EventBasedProcess();
        var engine     = new StateMachineEngine();
        var process    = new SchemataProcess();

        var instance = await engine.StartAsync(definition, process);
        Assert.Equal("New", instance.State);
        Assert.False(instance.IsComplete);

        process.State = instance.State;
        instance      = await engine.TriggerAsync(definition, process, definition.Pay, null);

        Assert.Equal("Processing", instance.State);
        Assert.False(instance.IsComplete);
    }

    [Fact]
    public async SystemTask EventBasedGateway_AwaitOnMessage_ConditionalBranch_True() {
        var definition = new EventBasedConditionalProcess();
        var engine     = new StateMachineEngine();
        var process    = new SchemataProcess { Variables = "{\"order\":{\"amount\":100}}" };

        var instance = await engine.StartAsync(definition, process);
        process.State = instance.State;

        instance = await engine.TriggerAsync(definition, process, definition.Pay, null);

        Assert.Equal("Processing", instance.State);
        Assert.False(instance.IsComplete);
    }

    [Fact]
    public async SystemTask EventBasedGateway_AwaitOnMessage_ConditionalBranch_False() {
        var definition = new EventBasedConditionalProcess();
        var engine     = new StateMachineEngine();
        var process    = new SchemataProcess { Variables = "{\"order\":{\"amount\":-10}}" };

        var instance = await engine.StartAsync(definition, process);
        process.State = instance.State;

        instance = await engine.TriggerAsync(definition, process, definition.Pay, null);

        Assert.Equal("Rejected", instance.State);
        Assert.False(instance.IsComplete);

        process.State = instance.State;
        instance      = await engine.AdvanceAsync(definition, process);

        Assert.Equal("RejectedEnd", instance.State);
        Assert.True(instance.IsComplete);
    }

    [Fact]
    public async SystemTask BoundaryEvent_CatchException_MatchesAndAdvances() {
        var definition = new BoundaryEventProcess();
        var engine     = new StateMachineEngine();
        var process    = new SchemataProcess();

        var instance = await engine.StartAsync(definition, process);
        Assert.Equal("Processing", instance.State);
        Assert.False(instance.IsComplete);

        process.State = instance.State;
        var errorDef = new ErrorDefinition {
            Name = typeof(TimeoutException).Name, ExceptionType = typeof(TimeoutException),
        };
        instance = await engine.TriggerAsync(definition, process, errorDef, null);

        Assert.Equal("Rejected", instance.State);
        Assert.False(instance.IsComplete);

        process.State = instance.State;
        instance      = await engine.AdvanceAsync(definition, process);

        Assert.Equal("RejectedEnd", instance.State);
        Assert.True(instance.IsComplete);
    }

    [Fact]
    public async SystemTask TypedCondition_WithJsonVariable_DeserializesAndMatches() {
        var definition = new TypedConditionProcess();
        var engine     = new StateMachineEngine();
        var process    = new SchemataProcess { Variables = "{\"order\":{\"amount\":100}}" };

        var instance = await engine.StartAsync(definition, process);
        process.State = instance.State;

        instance = await engine.AdvanceAsync(definition, process);

        Assert.Equal("Approved", instance.State);
        Assert.False(instance.IsComplete);
    }

    [Fact]
    public async SystemTask TypedCondition_WithJsonVariable_TakesOtherwisePath() {
        var definition = new TypedConditionProcess();
        var engine     = new StateMachineEngine();
        var process    = new SchemataProcess { Variables = "{\"order\":{\"amount\":10}}" };

        var instance = await engine.StartAsync(definition, process);
        process.State = instance.State;

        instance = await engine.AdvanceAsync(definition, process);

        Assert.Equal("Rejected", instance.State);
        Assert.False(instance.IsComplete);
    }

    [Fact]
    public async SystemTask AdvanceAsync_UnsupportedGateway_UsesElementNameInMessage() {
        var startEvent = new FlowEvent { Id       = "start", Name         = "Start", Position = EventPosition.Start };
        var gateway    = new ParallelGateway { Id = "parallel-join", Name = "Parallel Join" };
        var endEvent   = new FlowEvent { Id       = "end", Name           = "End", Position = EventPosition.End };
        var definition = new ProcessDefinition {
            Name     = "unsupported-gateway",
            Elements = { startEvent, gateway, endEvent },
            Flows = {
                new() { Id = "f1", Source = startEvent, Target = gateway },
                new() { Id = "f2", Source = gateway, Target    = endEvent },
            },
        };

        var ex = await Assert.ThrowsAsync<FailedPreconditionException>(() => new StateMachineEngine()
                                                                            .StartAsync(definition, new())
                                                                            .AsTask());

        Assert.Contains("Gateway 'Parallel Join'", ex.Message);
        Assert.DoesNotContain(nameof(ParallelGateway), ex.Message);
    }

    #region Nested type: BoundaryEventProcess

    private class BoundaryEventProcess : ProcessDefinition
    {
        public BoundaryEventProcess() {
            this.Start().Go(Processing);
            this.During(Processing).OnError<TimeoutException>().Go(Rejected);
            this.During(Processing).End();
            this.During(Rejected).End(RejectedEnd);
        }

        public NoneTask Processing  { get; } = null!;
        public NoneTask Rejected    { get; } = null!;
        public EndEvent RejectedEnd { get; } = null!;
    }

    #endregion

    #region Nested type: DslApprovalProcess

    private class DslApprovalProcess : ProcessDefinition
    {
        public DslApprovalProcess() {
            this.Start().Go(Draft);
            this.During(Draft).Go(Review);
            this.During(Review).End(Approved);
        }

        public UserTask Draft    { get; } = null!;
        public UserTask Review   { get; } = null!;
        public EndEvent Approved { get; } = null!;
    }

    #endregion

    #region Nested type: DslConditionalProcess

    private class DslConditionalProcess : ProcessDefinition
    {
        public DslConditionalProcess() {
            this.Start().Go(Draft);
            this.During(Draft)
                .Decide(
                     this.When(new LambdaConditionExpression {
                              Lambda = ctx => new(
                                  ctx.Variables.TryGetValue("amount", out var v)
                               && v is JsonElement je
                               && je.GetInt64() > 50),
                          })
                         .Go(Approved), this.Otherwise().Go(Rejected));
            this.During(Approved).End();
            this.During(Rejected).End();
        }

        public NoneTask Draft    { get; } = null!;
        public NoneTask Approved { get; } = null!;
        public NoneTask Rejected { get; } = null!;
    }

    #endregion

    #region Nested type: EventBasedConditionalProcess

    private class EventBasedConditionalProcess : ProcessDefinition
    {
        public EventBasedConditionalProcess() {
            this.Start().Go(New);
            this.During(New)
                .Await(this.On(Pay)
                           .Decide(this.When<Order>(o => o.Amount > 0).Go(Processing), this.Otherwise().Go(Rejected)));
            this.During(Processing).End();
            this.During(Rejected).End(RejectedEnd);
        }

        public NoneTask New         { get; } = null!;
        public NoneTask Processing  { get; } = null!;
        public NoneTask Rejected    { get; } = null!;
        public EndEvent RejectedEnd { get; } = null!;
        public Message  Pay         { get; } = null!;
    }

    #endregion

    #region Nested type: EventBasedProcess

    private class EventBasedProcess : ProcessDefinition
    {
        public EventBasedProcess() {
            this.Start().Go(New);
            this.During(New).Await(this.On(Pay).Go(Processing));
            this.During(Processing).End();
        }

        public NoneTask New        { get; } = null!;
        public NoneTask Processing { get; } = null!;
        public Message  Pay        { get; } = null!;
    }

    #endregion

    #region Nested type: Order

    private class Order
    {
        public long Amount { get; set; }
    }

    #endregion

    #region Nested type: TypedConditionProcess

    private class TypedConditionProcess : ProcessDefinition
    {
        public TypedConditionProcess() {
            this.Start().Go(Draft);
            this.During(Draft).Decide(this.When<Order>(o => o.Amount > 50).Go(Approved), this.Otherwise().Go(Rejected));
        }

        public NoneTask Draft    { get; } = null!;
        public NoneTask Approved { get; } = null!;
        public NoneTask Rejected { get; } = null!;
    }

    #endregion
}
