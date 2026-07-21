using System;
using System.Threading.Tasks;
using Schemata.Flow.Skeleton.Builders;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Integration.Tests.Fixtures;

public sealed class PersistTaskMutationProcess : ProcessDefinition
{
    public PersistTaskMutationProcess() {
        BindSource<Order>(projection: FlowSourceProjection.None);
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
        BindSource<Order>(order => order.State);
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
        BindSource<Order>(projection: FlowSourceProjection.None);
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
        BindSource<Order>(projection: FlowSourceProjection.None);
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
        BindSource<Order>(projection: FlowSourceProjection.None);
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

public sealed class IdempotencyProcess : ProcessDefinition
{
    public IdempotencyProcess() {
        this.Start().Go(Review);
        this.During(Review).End();
    }

    public UserTask Review { get; } = null!;
}

public abstract class CompensationProcess : ProcessDefinition
{
    protected CompensationProcess(bool throwsCompensation) {
        var start = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var host  = new NoneTask { Name = "host" };
        var afterHost = new NoneTask { Name = "after-host" };
        var after = new NoneTask { Name = "after" };
        var end = new FlowEvent { Name = "end", Position = EventPosition.End };
        var boundary = new FlowEvent {
            Name         = "compensate-host",
            Position     = EventPosition.Boundary,
            AttachedTo   = host,
            Definition   = new CompensationDefinition { Name = "compensate-host", Activity = host },
        };
        var undo = new NoneTask { Name = "undo-host" };

        Elements.AddRange([start, host, afterHost, after, end, boundary, undo]);
        Flows.Add(new() { Source = start, Target = host });
        Flows.Add(new() { Source = host, Target = afterHost });

        if (throwsCompensation) {
            var throwEvent = new FlowEvent {
                Name       = "throw",
                Position   = EventPosition.IntermediateThrow,
                Definition = new CompensationDefinition { Name = "compensate" },
            };
            Elements.Add(throwEvent);
            Flows.Add(new() { Source = afterHost, Target = throwEvent });
            Flows.Add(new() { Source = throwEvent, Target = after });
        } else {
            Flows.Add(new() { Source = afterHost, Target = after });
        }

        Flows.Add(new() { Source = after, Target = end });
        Flows.Add(new() { Source = boundary, Target = undo });
    }
}

public sealed class CompensationReloadProcess : CompensationProcess
{
    public CompensationReloadProcess() : base(true) { }
}

public sealed class CompensationTerminalProcess : CompensationProcess
{
    public CompensationTerminalProcess() : base(false) { }
}
