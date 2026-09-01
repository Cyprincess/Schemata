using Schemata.Flow.Skeleton.Builders;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Integration.Tests.Fixtures;

public sealed class ApprovalProcess : ProcessDefinition
{
    public ApprovalProcess() {
        BindSource<Order>(projection: FlowSourceProjection.None);
        this.Start().Go(Review);
        this.During(Review).Await(
            this.On(Payment).Decide(
                this.When<Order, ApprovalPayload>(Payment, (_, payload) => payload.Approved).Go(Approved),
                this.Otherwise().Go(Rejected)));
        this.During(Approved).End();
        this.During(Rejected).End();
    }

    public NoneTask Review { get; } = null!;

    public UserTask Approved { get; } = null!;

    public UserTask Rejected { get; } = null!;

    public Message<ApprovalPayload> Payment { get; } = null!;
}