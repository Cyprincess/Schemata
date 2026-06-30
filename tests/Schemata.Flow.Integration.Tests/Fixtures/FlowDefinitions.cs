using System;
using System.Threading.Tasks;
using Schemata.Flow.Skeleton.Builders;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Integration.Tests.Fixtures;

public sealed class PersistTaskMutationProcess : ProcessDefinition
{
    public PersistTaskMutationProcess() {
        this.BindSource<Order>(projection: FlowSourceProjection.None);
        this.Start().Go(Review);
        this.During(Review).Go(Apply);
        this.During(Apply).OnEnter<Order>(Mutate).End();
    }

    public UserTask Review { get; } = null!;
    public UserTask Apply  { get; } = null!;

    private static ValueTask Mutate(FlowTaskContext _, Order order) {
        order.TaskValue = "task-written";
        return ValueTask.CompletedTask;
    }
}

public sealed class ProjectionProcess : ProcessDefinition
{
    public ProjectionProcess() {
        this.BindSource<Order>(order => order.State);
        this.Start().Go(Review);
        this.During(Review).Go(Apply);
        this.During(Apply).OnEnter<Order>(Mutate).End();
    }

    public UserTask Review { get; } = null!;
    public UserTask Apply  { get; } = null!;

    private static ValueTask Mutate(FlowTaskContext _, Order order) {
        order.TaskValue = "task-written";
        return ValueTask.CompletedTask;
    }
}

public sealed class ConditionProcess : ProcessDefinition
{
    public ConditionProcess() {
        this.BindSource<Order>(projection: FlowSourceProjection.None);
        this.Start().Go(Review);
        this.During(Review).Decide(
            this.When<Order>(order => order.State == "new").Go(Accepted),
            this.Otherwise().Go(Rejected));
        this.During(Accepted).End();
        this.During(Rejected).End();
    }

    public UserTask Review   { get; } = null!;
    public UserTask Accepted { get; } = null!;
    public UserTask Rejected { get; } = null!;
}

public sealed class FailingTaskProcess : ProcessDefinition
{
    public FailingTaskProcess() {
        this.BindSource<Order>(projection: FlowSourceProjection.None);
        this.Start().Go(Review);
        this.During(Review).Go(Fail);
        this.During(Fail).OnEnter<Order>(MutateThenFailAsync).End();
    }

    public UserTask Review { get; } = null!;
    public UserTask Fail   { get; } = null!;

    private static async ValueTask MutateThenFailAsync(FlowTaskContext context, Order order) {
        order.TaskValue = "rolled-back";
        await context.BindSourceAsync("temporary", order);
        throw new InvalidOperationException("Expected integration test failure.");
    }
}

public sealed class BranchWriteProcess : ProcessDefinition
{
    public BranchWriteProcess() {
        this.BindSource<Order>(projection: FlowSourceProjection.None);
        this.Start().Go(Review);
        this.During(Review).Go(Apply);
        this.During(Apply).OnEnter<Order>(Mutate).End();
    }

    public UserTask Review { get; } = null!;
    public UserTask Apply  { get; } = null!;

    private static ValueTask Mutate(FlowTaskContext _, Order order) {
        order.TaskValue = "branch-written";
        return ValueTask.CompletedTask;
    }
}
