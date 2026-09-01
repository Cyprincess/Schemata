using System;
using System.Threading.Tasks;
using Schemata.Flow.Skeleton.Builders;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Integration.Tests.Fixtures;

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