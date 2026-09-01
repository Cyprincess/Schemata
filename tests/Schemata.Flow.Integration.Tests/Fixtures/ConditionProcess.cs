using Schemata.Flow.Skeleton.Builders;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Integration.Tests.Fixtures;

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