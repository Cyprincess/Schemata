using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Advisors;
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

public class MultiStateMessageShould
{
    [Fact]
    public void Validator_AcceptsSameMessage_AcrossMultipleAwaitStates() {
        var definition = new MultiAwaitProcess();

        var ex = Record.Exception(() => StateMachineValidator.Validate(definition));

        Assert.Null(ex);
    }

    [Fact]
    public void Validator_AcceptsSameMessage_OnBoundary_AcrossMultipleActivities() {
        var definition = new MultiBoundaryProcess();

        var ex = Record.Exception(() => StateMachineValidator.Validate(definition));

        Assert.Null(ex);
    }

    [Fact]
    public async Task Engine_LookupsByTokenState_RejectsWhenTokenIsNotAtAwait() {
        var definition = new MultiAwaitProcess();
        var engine     = new StateMachineEngine();
        var context    = ExecutionContext();
        var process    = new SchemataProcess { Name = "p1", CanonicalName = "processes/p1" };
        var token = new SchemataProcessToken {
            Name          = "t1",
            CanonicalName = "processes/p1/tokens/t1",
            Process       = "p1",
            ScopeName     = "p1",
            StateName     = "irrelevant-state",
            State         = "Active",
        };

        var ex = await Assert.ThrowsAsync<InvalidArgumentException>(() =>
            engine.TriggerAsync(definition, process, [token], context, definition.Cancel, null).AsTask());
        Assert.Contains("Cancel", ex.Message);
    }

    [Fact]
    public async Task Engine_ResolvesSameMessage_FromDifferentStates_ToTheirOwnTargets() {
        var definition = new MultiAwaitProcess();
        var engine     = new StateMachineEngine();
        var context    = ExecutionContext();
        var process    = new SchemataProcess { Name = "p1", CanonicalName = "processes/p1" };

        var fromA = await engine.TriggerAsync(
            definition, process,
            [Token("processes/p1/tokens/a", definition.A.Name, "Await_A")],
            context, definition.Cancel, null);

        var fromB = await engine.TriggerAsync(
            definition, process,
            [Token("processes/p1/tokens/b", definition.B.Name, "Await_B")],
            context, definition.Cancel, null);

        Assert.Equal(definition.CancelledFromA.Name, fromA.Tokens[0].StateName);
        Assert.Equal(definition.CancelledFromB.Name, fromB.Tokens[0].StateName);
    }

    [Fact]
    public async Task Engine_Returns_Waiting_Token_As_Trigger_Target() {
        var definition = new MultiAwaitProcess();
        var engine     = new StateMachineEngine();
        var process    = new SchemataProcess { Name = "p1", CanonicalName = "processes/p1" };
        var token      = Token("processes/p1/tokens/a", definition.A.Name, "Await_A");

        var targets = await engine.FindTriggerTargetsAsync(definition, process, [token], ExecutionContext(), definition.Cancel);

        Assert.Equal([token.CanonicalName!], targets);
    }

    [Fact]
    public async Task Engine_Evaluates_Source_Condition_Against_Bound_Order() {
        var definition = new SourceDecisionProcess();
        var engine = new StateMachineEngine();
        var order = new Order { Name = "o1", CanonicalName = "orders/o1", State = "paid" };
        var services = new ServiceCollection()
                      .AddSingleton(Repository(new SchemataProcessSource {
                          Process = "processes/p1", Name = "order", SourceType = typeof(Order).FullName ?? typeof(Order).Name, Source = order.CanonicalName!,
                      }).Object)
                      .AddSingleton(Repository(order).Object)
                      .BuildServiceProvider();
        var process = new SchemataProcess { Name = "p1", CanonicalName = "processes/p1" };
        var token = Token("processes/p1/tokens/a", definition.Review.Name);

        var snapshot = await engine.AdvanceAsync(definition, process, [token], new(Mock.Of<IUnitOfWork>(), services));

        Assert.Equal(definition.Paid.Name, snapshot.Tokens[0].StateName);
    }

    [Fact]
    public async Task Engine_Runs_Enter_Before_Parking_And_Leave_After_Advancing() {
        var log = new List<string>();
        var definition = new EntryExitProcess(log);
        var engine = new StateMachineEngine();
        var process = new SchemataProcess { Name = "p1", CanonicalName = "processes/p1" };
        var context = ExecutionContext();

        var started = await engine.StartAsync(definition, process, context);
        var advanced = await engine.AdvanceAsync(definition, process, started.Tokens, context);

        Assert.Equal(["enter", "leave"], log);
        Assert.Equal(definition.Next.Name, advanced.Tokens[0].StateName);
    }

    [Fact]
    public void Validator_Rejects_Typed_Procedure_Task_Reached_From_Different_Payload() {
        var definition = new PayloadMismatchProcess();

        var ex = Assert.Throws<InvalidOperationException>(() => StateMachineValidator.Validate(definition));

        Assert.Contains("Typed procedure task payload mismatch", ex.Message);
    }

    private static SchemataProcessToken Token(string canonical, string stateName, string? waitingAtName = null) {
        return new() {
            Name          = canonical[(canonical.LastIndexOf('/') + 1)..],
            CanonicalName = canonical,
            Process       = "p1",
            ScopeName     = "p1",
            StateName     = stateName,
            WaitingAtName = waitingAtName,
            State         = waitingAtName is not null ? "Waiting" : "Active",
        };
    }

    private static FlowExecutionContext ExecutionContext() {
        return new(Mock.Of<IUnitOfWork>(), new ServiceCollection().BuildServiceProvider());
    }

    private static Mock<IRepository<T>> Repository<T>(params T[] items)
        where T : class {
        var data = items.ToList();
        var repository = new Mock<IRepository<T>>();
        repository.Setup(r => r.Join(It.IsAny<IUnitOfWork>()));
        repository.Setup(r => r.AdviceContext).Returns(new AdviceContext(new ServiceCollection().BuildServiceProvider()));
        repository.Setup(r => r.SingleOrDefaultAsync(It.IsAny<Func<IQueryable<T>, IQueryable<T>>>(), It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<T>, IQueryable<T>> predicate, CancellationToken _) => new(predicate(data.AsQueryable()).SingleOrDefault()));
        return repository;
    }

    #region Nested type: MultiAwaitProcess

    private sealed class MultiAwaitProcess : ProcessDefinition
    {
        public MultiAwaitProcess() {
            // The Pre activity exists only so the same Cancel message can be awaited from two
            // reachable states (A and B). Pre.Await routes to A via Continue; B is reached
            // by completing A normally through CompleteA.
            this.Start().Go(Pre);
            this.During(Pre).Await(this.On(Continue).Go(A));
            this.During(A).Await(this.On(Cancel).Go(CancelledFromA), this.On(CompleteA).Go(B));
            this.During(B).Await(this.On(Cancel).Go(CancelledFromB));
            this.During(CancelledFromA).End();
            this.During(CancelledFromB).End();
        }

        public UserTask Pre            { get; } = null!;
        public UserTask A              { get; } = null!;
        public UserTask B              { get; } = null!;
        public UserTask CancelledFromA { get; } = null!;
        public UserTask CancelledFromB { get; } = null!;
        public Message  Continue       { get; } = null!;
        public Message  CompleteA      { get; } = null!;
        public Message  Cancel         { get; } = null!;
    }

    #endregion

    #region Nested type: MultiBoundaryProcess

    private sealed class MultiBoundaryProcess : ProcessDefinition
    {
        public MultiBoundaryProcess() {
            this.Start().Go(A);
            this.During(A).OnMessage(Cancel).Go(CancelledFromA);
            this.During(A).Go(B);
            this.During(B).OnMessage(Cancel).Go(CancelledFromB);
            this.During(B).End();
            this.During(CancelledFromA).End();
            this.During(CancelledFromB).End();
        }

        public UserTask A              { get; } = null!;
        public UserTask B              { get; } = null!;
        public UserTask CancelledFromA { get; } = null!;
        public UserTask CancelledFromB { get; } = null!;
        public Message  Cancel         { get; } = null!;
    }

    #endregion

    #region Nested type: SourceDecisionProcess

    private sealed class SourceDecisionProcess : ProcessDefinition
    {
        public SourceDecisionProcess() {
            this.Start().Go(Review);
            this.During(Review).Decide(
                this.When<Order>(order => order.State == "paid").Go(Paid),
                this.Otherwise().Go(Rejected));
            this.During(Paid).End();
            this.During(Rejected).End();
        }

        public UserTask Review { get; } = null!;

        public UserTask Paid { get; } = null!;

        public UserTask Rejected { get; } = null!;
    }

    #endregion

    #region Nested type: EntryExitProcess

    private sealed class EntryExitProcess : ProcessDefinition
    {
        public EntryExitProcess(List<string> log) {
            this.Start().Go(Current);
            this.During(Current)
                .OnEnter(_ => {
                    log.Add("enter");
                    return ValueTask.CompletedTask;
                })
                .OnLeave(_ => {
                    log.Add("leave");
                    return ValueTask.CompletedTask;
                })
                .Go(Next);
            this.During(Next).End();
        }

        public UserTask Current { get; } = null!;

        public UserTask Next { get; } = null!;
    }

    #endregion

    #region Nested type: PayloadMismatchProcess

    private sealed class PayloadMismatchProcess : ProcessDefinition
    {
        public PayloadMismatchProcess() {
            this.Start().Go(Waiting);
            this.During(Waiting).Await(this.On(Submit).Go(Handle));
            this.During(Handle).End();
        }

        public UserTask Waiting { get; } = null!;

        public ProcedureTask<int> Handle { get; } = null!;

        public Message<string> Submit { get; } = null!;
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
