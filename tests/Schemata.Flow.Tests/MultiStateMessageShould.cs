using Schemata.Abstractions.Exceptions;
using Schemata.Flow.Skeleton;
using Schemata.Flow.Skeleton.Builders;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.StateMachine;
using Xunit;
using SystemTask = System.Threading.Tasks.Task;

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
    public async SystemTask Engine_LookupsByIdOnly_StateNameAloneIsNotEnough() {
        var definition = new MultiAwaitProcess();
        var engine     = new StateMachineEngine();

        // StateId is the Flow-internal identifier. Without it the engine cannot resolve the
        // current element from the user-facing State display label alone, so the trigger is
        // reported as invalid from this state.
        var process = new SchemataProcess { State = "A" };

        var ex = await Assert.ThrowsAsync<InvalidArgumentException>(() =>
            engine.TriggerAsync(definition, process, definition.Cancel, null).AsTask());
        Assert.Contains("Cancel", ex.Message);
    }

    [Fact]
    public async SystemTask Engine_ResolvesSameMessage_FromDifferentStates_ToTheirOwnTargets() {
        var definition = new MultiAwaitProcess();
        var engine     = new StateMachineEngine();

        var fromA = await engine.TriggerAsync(
            definition,
            new SchemataProcess { StateId = definition.A.Id },
            definition.Cancel,
            null);

        var fromB = await engine.TriggerAsync(
            definition,
            new SchemataProcess { StateId = definition.B.Id },
            definition.Cancel,
            null);

        Assert.Equal(definition.CancelledFromA.Id, fromA.StateId);
        Assert.Equal(definition.CancelledFromB.Id, fromB.StateId);
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
}
