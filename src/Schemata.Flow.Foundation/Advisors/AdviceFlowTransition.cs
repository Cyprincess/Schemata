using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Flow.Skeleton.Advisors;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Flow.Foundation.Advisors;

public sealed class AdviceFlowTransition : IFlowTransitionAdvisor
{
    public const int DefaultOrder = Orders.Base;

    #region IFlowTransitionAdvisor Members

    public int Order => DefaultOrder;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext         ctx,
        FlowTransitionContext transition,
        CancellationToken     ct = default
    ) {
        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
