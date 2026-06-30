using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.Repository;
using Schemata.Flow.Skeleton.Builders;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;
using Schemata.Flow.StateMachine;
using Xunit;

namespace Schemata.Flow.Tests;

public class NoneTaskPassThroughShould
{
    [Fact]
    public async Task Start_Parks_Awaiting_None_Task_At_Its_Gateway() {
        var definition = new OrderProcess();
        var engine     = new StateMachineEngine();
        var process    = new SchemataProcess { Name = "p1", CanonicalName = "processes/p1" };

        var started = await engine.StartAsync(definition, process, Context());

        var token = started.Tokens[0];
        Assert.Equal(definition.New.Name, token.StateName);
        Assert.Equal("Await_New", token.WaitingAtName);
        Assert.Equal("Waiting", token.State);
        Assert.Equal("Waiting", process.State);

        var transition = Assert.Single(started.Transitions);
        Assert.Null(transition.Previous);
        Assert.Equal(definition.New.Name, transition.Posterior);
    }

    [Fact]
    public async Task Trigger_Correlates_Parked_None_Task_Without_Explicit_Advance() {
        var definition = new OrderProcess();
        var engine     = new StateMachineEngine();
        var process    = new SchemataProcess { Name = "p1", CanonicalName = "processes/p1" };
        var context    = Context();

        var started   = await engine.StartAsync(definition, process, context);
        var triggered = await engine.TriggerAsync(definition, process, started.Tokens, context, definition.Pay, null);

        var token = triggered.Tokens[0];
        Assert.Equal(definition.Fulfill.Name, token.StateName);
        Assert.Null(token.WaitingAtName);
        Assert.Equal("Completed", token.State);
        Assert.Equal("Completed", process.State);

        var transition = Assert.Single(triggered.Transitions);
        Assert.Equal(definition.New.Name, transition.Previous);
        Assert.Equal(definition.Fulfill.Name, transition.Posterior);
        Assert.Equal(definition.Pay.Name, transition.Event);
    }

    [Fact]
    public async Task Trigger_Chains_Into_Next_Awaiting_None_Task_In_One_Step() {
        var definition = new ChainProcess();
        var engine     = new StateMachineEngine();
        var process    = new SchemataProcess { Name = "p1", CanonicalName = "processes/p1" };
        var context    = Context();

        var started = await engine.StartAsync(definition, process, context);
        var shipped = await engine.TriggerAsync(definition, process, started.Tokens, context, definition.Pay, null);

        Assert.Equal(definition.Shipped.Name, shipped.Tokens[0].StateName);
        Assert.Equal("Await_Shipped", shipped.Tokens[0].WaitingAtName);
        Assert.Equal("Waiting", shipped.Tokens[0].State);

        var done = await engine.TriggerAsync(definition, process, shipped.Tokens, context, definition.Deliver, null);

        Assert.Equal(definition.Done.Name, done.Tokens[0].StateName);
        Assert.Equal("Completed", done.Tokens[0].State);
        Assert.Equal("Completed", process.State);
    }

    [Fact]
    public async Task Advance_Returns_Parked_None_Task_Unchanged() {
        var definition = new OrderProcess();
        var engine     = new StateMachineEngine();
        var process    = new SchemataProcess { Name = "p1", CanonicalName = "processes/p1" };
        var context    = Context();

        var started  = await engine.StartAsync(definition, process, context);
        var advanced = await engine.AdvanceAsync(definition, process, started.Tokens, context);

        Assert.Empty(advanced.Transitions);
        Assert.Equal(definition.New.Name, advanced.Tokens[0].StateName);
        Assert.Equal("Await_New", advanced.Tokens[0].WaitingAtName);
        Assert.Equal("Waiting", advanced.Tokens[0].State);
    }

    [Fact]
    public async Task Start_Runs_Enter_Task_Before_Parking() {
        var log        = new List<string>();
        var definition = new EnteredProcess(log);
        var engine     = new StateMachineEngine();
        var process    = new SchemataProcess { Name = "p1", CanonicalName = "processes/p1" };

        var started = await engine.StartAsync(definition, process, Context());

        Assert.Equal(["enter"], log);
        Assert.Equal(definition.New.Name, started.Tokens[0].StateName);
        Assert.Equal("Await_New", started.Tokens[0].WaitingAtName);
        Assert.Equal("Waiting", started.Tokens[0].State);
    }

    [Fact]
    public async Task User_Task_Still_Requires_Advance_Before_Trigger() {
        var definition = new ApprovalProcess();
        var engine     = new StateMachineEngine();
        var process    = new SchemataProcess { Name = "p1", CanonicalName = "processes/p1" };
        var context    = Context();

        var started = await engine.StartAsync(definition, process, context);

        Assert.Equal(definition.Review.Name, started.Tokens[0].StateName);
        Assert.Null(started.Tokens[0].WaitingAtName);
        Assert.Equal("Active", started.Tokens[0].State);

        await Assert.ThrowsAsync<InvalidArgumentException>(() =>
            engine.TriggerAsync(definition, process, started.Tokens, context, definition.Approved, null).AsTask());

        var waiting = await engine.AdvanceAsync(definition, process, started.Tokens, context);

        Assert.Equal(definition.Review.Name, waiting.Tokens[0].StateName);
        Assert.Equal("Await_Review", waiting.Tokens[0].WaitingAtName);
        Assert.Equal("Waiting", waiting.Tokens[0].State);

        var triggered = await engine.TriggerAsync(definition, process, waiting.Tokens, context, definition.Approved, null);

        Assert.Equal(definition.Ship.Name, triggered.Tokens[0].StateName);
        Assert.Equal("Active", triggered.Tokens[0].State);
        Assert.Equal(definition.Review.Name, Assert.Single(triggered.Transitions).Previous);
    }

    [Fact]
    public void Validator_Rejects_Boundary_On_Awaiting_None_Task() {
        var definition = new BoundaryOnAwaitingNoneTaskProcess();

        var ex = Assert.Throws<FailedPreconditionException>(() => StateMachineValidator.Validate(definition));

        Assert.Contains("can never fire", ex.Message);
    }

    [Fact]
    public void Validator_Rejects_Boundary_On_Terminal_None_Task() {
        var definition = new BoundaryOnTerminalNoneTaskProcess();

        var ex = Assert.Throws<FailedPreconditionException>(() => StateMachineValidator.Validate(definition));

        Assert.Contains("can never fire", ex.Message);
    }

    [Fact]
    public void Validator_Accepts_Boundary_On_Awaiting_User_Task() {
        var definition = new BoundaryOnAwaitingUserTaskProcess();

        var ex = Record.Exception(() => StateMachineValidator.Validate(definition));

        Assert.Null(ex);
    }

    [Fact]
    public void Validator_Accepts_Boundary_On_None_Task_With_Direct_Flow() {
        var definition = new BoundaryOnDirectNoneTaskProcess();

        var ex = Record.Exception(() => StateMachineValidator.Validate(definition));

        Assert.Null(ex);
    }

    private static FlowExecutionContext Context() {
        return new(Mock.Of<IUnitOfWork>(), new ServiceCollection().BuildServiceProvider());
    }

    #region Nested type: OrderProcess

    private sealed class OrderProcess : ProcessDefinition
    {
        public OrderProcess() {
            this.Start().Go(New);
            this.During(New).Await(
                this.On(Pay).Go(Fulfill),
                this.On(Shutdown).Go(Cancel));
            this.During(Fulfill).End();
            this.During(Cancel).End();
        }

        public NoneTask New      { get; } = null!;
        public NoneTask Fulfill  { get; } = null!;
        public NoneTask Cancel   { get; } = null!;
        public Message  Pay      { get; } = null!;
        public Signal   Shutdown { get; } = null!;
    }

    #endregion

    #region Nested type: ChainProcess

    private sealed class ChainProcess : ProcessDefinition
    {
        public ChainProcess() {
            this.Start().Go(New);
            this.During(New).Await(this.On(Pay).Go(Shipped));
            this.During(Shipped).Await(this.On(Deliver).Go(Done));
            this.During(Done).End();
        }

        public NoneTask New     { get; } = null!;
        public NoneTask Shipped { get; } = null!;
        public NoneTask Done    { get; } = null!;
        public Message  Pay     { get; } = null!;
        public Message  Deliver { get; } = null!;
    }

    #endregion

    #region Nested type: EnteredProcess

    private sealed class EnteredProcess : ProcessDefinition
    {
        public EnteredProcess(List<string> log) {
            this.Start().Go(New);
            this.During(New)
                .OnEnter(_ => {
                    log.Add("enter");
                    return ValueTask.CompletedTask;
                })
                .Await(this.On(Pay).Go(Done));
            this.During(Done).End();
        }

        public NoneTask New  { get; } = null!;
        public NoneTask Done { get; } = null!;
        public Message  Pay  { get; } = null!;
    }

    #endregion

    #region Nested type: ApprovalProcess

    private sealed class ApprovalProcess : ProcessDefinition
    {
        public ApprovalProcess() {
            this.Start().Go(Review);
            this.During(Review).Await(this.On(Approved).Go(Ship));
            this.During(Ship).End();
        }

        public UserTask Review   { get; } = null!;
        public UserTask Ship     { get; } = null!;
        public Message  Approved { get; } = null!;
    }

    #endregion

    #region Nested type: BoundaryOnAwaitingNoneTaskProcess

    private sealed class BoundaryOnAwaitingNoneTaskProcess : ProcessDefinition
    {
        public BoundaryOnAwaitingNoneTaskProcess() {
            this.Start().Go(New);
            this.During(New).Await(this.On(Pay).Go(Done));
            this.During(New).OnMessage(Cancel).Go(Dead);
            this.During(Done).End();
            this.During(Dead).End();
        }

        public NoneTask New    { get; } = null!;
        public NoneTask Done   { get; } = null!;
        public NoneTask Dead   { get; } = null!;
        public Message  Pay    { get; } = null!;
        public Message  Cancel { get; } = null!;
    }

    #endregion

    #region Nested type: BoundaryOnTerminalNoneTaskProcess

    private sealed class BoundaryOnTerminalNoneTaskProcess : ProcessDefinition
    {
        public BoundaryOnTerminalNoneTaskProcess() {
            this.Start().Go(New);
            this.During(New).End();
            this.During(New).OnMessage(Cancel).Go(Dead);
            this.During(Dead).End();
        }

        public NoneTask New    { get; } = null!;
        public NoneTask Dead   { get; } = null!;
        public Message  Cancel { get; } = null!;
    }

    #endregion

    #region Nested type: BoundaryOnAwaitingUserTaskProcess

    private sealed class BoundaryOnAwaitingUserTaskProcess : ProcessDefinition
    {
        public BoundaryOnAwaitingUserTaskProcess() {
            this.Start().Go(Review);
            this.During(Review).Await(this.On(Pay).Go(Done));
            this.During(Review).OnMessage(Cancel).Go(Dead);
            this.During(Done).End();
            this.During(Dead).End();
        }

        public UserTask Review { get; } = null!;
        public UserTask Done   { get; } = null!;
        public UserTask Dead   { get; } = null!;
        public Message  Pay    { get; } = null!;
        public Message  Cancel { get; } = null!;
    }

    #endregion

    #region Nested type: BoundaryOnDirectNoneTaskProcess

    private sealed class BoundaryOnDirectNoneTaskProcess : ProcessDefinition
    {
        public BoundaryOnDirectNoneTaskProcess() {
            this.Start().Go(New);
            this.During(New).Go(Next);
            this.During(New).OnMessage(Cancel).Go(Dead);
            this.During(Next).End();
            this.During(Dead).End();
        }

        public NoneTask New    { get; } = null!;
        public UserTask Next   { get; } = null!;
        public NoneTask Dead   { get; } = null!;
        public Message  Cancel { get; } = null!;
    }

    #endregion
}
