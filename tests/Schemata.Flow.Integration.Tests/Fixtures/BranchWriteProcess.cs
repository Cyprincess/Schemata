using System.Threading.Tasks;
using Schemata.Flow.Skeleton.Builders;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Integration.Tests.Fixtures;

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