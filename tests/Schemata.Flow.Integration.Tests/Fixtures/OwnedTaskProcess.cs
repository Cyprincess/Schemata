using System.Threading.Tasks;
using Schemata.Flow.Skeleton.Builders;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Integration.Tests.Fixtures;

public sealed class OwnedTaskProcess : ProcessDefinition
{
    public OwnedTaskProcess() {
        BindSource<OwnedOrder>(projection: FlowSourceProjection.None);
        this.Start().Go(Review);
        this.During(Review).Go(Apply);
        this.During(Apply).OnEnter<OwnedOrder>(Mutate).End();
    }

    public UserTask Review { get; } = null!;
    public UserTask Apply  { get; } = null!;

    private static ValueTask Mutate(FlowTaskContext _, OwnedOrder order) {
        order.TaskValue = "touched";
        return ValueTask.CompletedTask;
    }
}