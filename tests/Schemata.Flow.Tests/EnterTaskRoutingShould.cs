using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.Repository;
using Schemata.Flow.Skeleton.Builders;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;
using Schemata.Flow.StateMachine;
using Xunit;

namespace Schemata.Flow.Tests;

public class EnterTaskRoutingShould
{
    [Fact]
    public void Route_Await_Edge_Declared_After_On_Enter_Through_Enter_Task() {
        var definition = new LateAwaitEdgeProcess([]);

        var enter      = definition.Elements.Single(e => e.Name == "Enter_Target");
        var catchEvent = definition.Elements.OfType<FlowEvent>()
                                   .Single(e => e.Position == EventPosition.IntermediateCatch);

        Assert.Same(enter, definition.Flows.Single(f => f.Source == catchEvent).Target);
        Assert.Same(enter, definition.Flows.Single(f => f.Target == definition.Target).Source);
    }

    [Fact]
    public async Task Run_Enter_Body_For_Edge_Declared_After_On_Enter() {
        var log        = new List<string>();
        var definition = new LateAwaitEdgeProcess(log);
        var engine     = new StateMachineEngine();
        var process    = new SchemataProcess { Name = "p1", CanonicalName = "processes/p1" };
        var context    = Context();

        var started = await engine.StartAsync(definition, process, context);
        var waiting = await engine.AdvanceAsync(definition, process, started.Tokens, context);
        var entered = await engine.TriggerAsync(definition, process, waiting.Tokens, context, definition.Go, null);

        Assert.Equal(["enter"], log);
        Assert.Equal(definition.Target.Name, entered.Tokens[0].StateName);
    }

    [Fact]
    public void Produce_Identical_Graphs_Regardless_Of_Declaration_Order() {
        var early = new EarlyAwaitEdgeProcess([]);
        var late  = new LateAwaitEdgeProcess([]);

        Assert.Equal(
            early.AllElements.Select(e => e.Name).OrderBy(n => n, StringComparer.Ordinal).ToList(),
            late.AllElements.Select(e => e.Name).OrderBy(n => n, StringComparer.Ordinal).ToList());

        Assert.Equal(
            early.AllFlows.Select(f => $"{f.Source.Name}->{f.Target.Name}").OrderBy(n => n, StringComparer.Ordinal).ToList(),
            late.AllFlows.Select(f => $"{f.Source.Name}->{f.Target.Name}").OrderBy(n => n, StringComparer.Ordinal).ToList());
    }

    [Fact]
    public void Route_Start_Edge_Declared_After_On_Enter_Through_Enter_Task() {
        var definition = new LateStartEdgeProcess();

        var startEvent = definition.Elements.OfType<FlowEvent>().Single(e => e.Position == EventPosition.Start);
        var outgoing   = definition.Flows.Single(f => f.Source == startEvent);

        Assert.Equal("Enter_Only", outgoing.Target.Name);
    }

    [Fact]
    public void Route_Decision_Branch_Declared_After_On_Enter_Through_Enter_Task() {
        var definition = new LateDecisionEdgeProcess();

        var gateway  = definition.Elements.OfType<ExclusiveGateway>().Single();
        var branches = definition.Flows.Where(f => f.Source == gateway).ToList();

        Assert.Contains(branches, f => f.Target.Name == "Enter_Target");
        Assert.DoesNotContain(branches, f => f.Target == definition.Target);
    }

    [Fact]
    public void Route_Boundary_Edge_Declared_After_On_Enter_Through_Enter_Task() {
        var definition = new LateBoundaryEdgeProcess();

        var boundary = definition.Elements.OfType<FlowEvent>().Single(e => e.Position == EventPosition.Boundary);
        var outgoing = definition.Flows.Single(f => f.Source == boundary);

        Assert.Equal("Enter_Target", outgoing.Target.Name);
    }

    [Fact]
    public void Validator_Accepts_Normalized_Enter_Task_Routing() {
        var definition = new LateAwaitEdgeProcess([]);

        var ex = Record.Exception(() => StateMachineValidator.Validate(definition));

        Assert.Null(ex);
    }

    [Fact]
    public void Validator_Rejects_Direct_Edge_Bypassing_Enter_Task() {
        // Uses the early-order graph so Enter_Target stays reachable through the routed edge and
        // only the manually added bypass edge is at fault (reachability cannot catch it).
        var definition = new EarlyAwaitEdgeProcess([]);
        var catchEvent = definition.Elements.OfType<FlowEvent>()
                                   .Single(e => e.Position == EventPosition.IntermediateCatch);
        definition.Flows.Add(new() { Source = catchEvent, Target = definition.Target });

        var ex = Assert.Throws<FailedPreconditionException>(() => StateMachineValidator.Validate(definition));

        Assert.Contains("Enter_Target", ex.Message);
    }

    private static FlowExecutionContext Context() {
        return new(Mock.Of<IUnitOfWork>(), new ServiceCollection().BuildServiceProvider());
    }

    #region Nested type: LateAwaitEdgeProcess

    private sealed class LateAwaitEdgeProcess : ProcessDefinition
    {
        public LateAwaitEdgeProcess(List<string> log) {
            this.During(Target)
                .OnEnter(_ => {
                    log.Add("enter");
                    return ValueTask.CompletedTask;
                })
                .End();
            this.During(Waiting).Await(this.On(Go).Go(Target));
            this.Start().Go(Waiting);
        }

        public UserTask Waiting { get; } = null!;
        public UserTask Target  { get; } = null!;
        public Message  Go      { get; } = null!;
    }

    #endregion

    #region Nested type: EarlyAwaitEdgeProcess

    private sealed class EarlyAwaitEdgeProcess : ProcessDefinition
    {
        public EarlyAwaitEdgeProcess(List<string> log) {
            this.During(Waiting).Await(this.On(Go).Go(Target));
            this.During(Target)
                .OnEnter(_ => {
                    log.Add("enter");
                    return ValueTask.CompletedTask;
                })
                .End();
            this.Start().Go(Waiting);
        }

        public UserTask Waiting { get; } = null!;
        public UserTask Target  { get; } = null!;
        public Message  Go      { get; } = null!;
    }

    #endregion

    #region Nested type: LateStartEdgeProcess

    private sealed class LateStartEdgeProcess : ProcessDefinition
    {
        public LateStartEdgeProcess() {
            this.During(Only).OnEnter(_ => ValueTask.CompletedTask).End();
            this.Start().Go(Only);
        }

        public UserTask Only { get; } = null!;
    }

    #endregion

    #region Nested type: LateDecisionEdgeProcess

    private sealed class LateDecisionEdgeProcess : ProcessDefinition
    {
        public LateDecisionEdgeProcess() {
            this.During(Target).OnEnter(_ => ValueTask.CompletedTask).End();
            this.During(Review).Decide(
                this.When<Order>(order => order.State == "paid").Go(Target),
                this.Otherwise().Go(Rejected));
            this.During(Rejected).End();
            this.Start().Go(Review);
        }

        public UserTask Review   { get; } = null!;
        public UserTask Target   { get; } = null!;
        public UserTask Rejected { get; } = null!;
    }

    #endregion

    #region Nested type: LateBoundaryEdgeProcess

    private sealed class LateBoundaryEdgeProcess : ProcessDefinition
    {
        public LateBoundaryEdgeProcess() {
            this.During(Target).OnEnter(_ => ValueTask.CompletedTask).End();
            this.During(Work).OnMessage(Cancel).Go(Target);
            this.During(Work).End();
            this.Start().Go(Work);
        }

        public UserTask Work   { get; } = null!;
        public UserTask Target { get; } = null!;
        public Message  Cancel { get; } = null!;
    }

    #endregion

    #region Nested type: Order

    public sealed class Order : ICanonicalName
    {
        public string? State { get; set; }

        public string? Name { get; set; }

        public string? CanonicalName { get; set; }
    }

    #endregion
}
