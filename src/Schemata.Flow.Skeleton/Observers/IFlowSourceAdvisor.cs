using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;

namespace Schemata.Flow.Skeleton.Observers;

/// <summary>
///     Runs inside a flow transition for a bound source entity type.
/// </summary>
/// <typeparam name="TSource">The source entity type bound to the process or token.</typeparam>
public interface IFlowSourceAdvisor<TSource> : IAdvisor<FlowTransitionContext, TSource>
    where TSource : class, ICanonicalName
{
}
