using Schemata.Abstractions.Advisors;
using Schemata.Entity.Repository;

namespace Schemata.Flow.Skeleton.Observers;

/// <summary>
///     Runs inside the transition's unit of work, before the process row is persisted. Advisors that
///     need their writes to commit atomically with the transition enlist their repositories with
///     <see cref="IRepository.Join" /> against <see cref="FlowTransitionContext.UnitOfWork" />;
///     advisors that only provision external infrastructure (timer jobs, broker subscriptions) may
///     ignore the unit of work. A throw aborts the transition before persistence, so DB writes that
///     joined the unit of work roll back; external writes that already completed remain.
///     Implementations return <see cref="AdviseResult.Continue" />.
/// </summary>
public interface IFlowTransitionAdvisor : IAdvisor<FlowTransitionContext>
{
}
