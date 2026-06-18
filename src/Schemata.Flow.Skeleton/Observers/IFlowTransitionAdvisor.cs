using Schemata.Abstractions.Advisors;

namespace Schemata.Flow.Skeleton.Observers;

/// <summary>
///     Trunk-required participant that provisions the wake-up infrastructure (timer jobs, event
///     subscriptions) a process depends on to leave a waiting state. The runtime runs these advisors
///     before the transition is committed, so a provisioning failure aborts the transition instead of
///     persisting an instance that waits forever on infrastructure that was never created.
///     Implementations provision only their own infrastructure and return
///     <see cref="AdviseResult.Continue" />.
/// </summary>
public interface IFlowTransitionAdvisor : IAdvisor<FlowTransitionContext>
{
}
