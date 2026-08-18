using Schemata.Abstractions.Advisors;
using Schemata.Entity.Repository;
using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Skeleton.Observers;

/// <summary>
///     Reacts to a transition inside its unit of work, before the process row is persisted. Advisors
///     that need their writes to commit atomically with the transition enlist their repositories with
///     <see cref="IRepository.Join" /> against <see cref="FlowTransitionContext.UnitOfWork" />;
///     advisors that only touch external systems may ignore the unit of work.
/// </summary>
/// <remarks>
///     Pick this contract for work that observes or vetoes a transition — auditing, read-model
///     projection, invariant enforcement. Pick <see cref="IFlowCatchHandler" /> instead to own the
///     delivery of a catch kind; that one is infrastructure the engine requires, so it runs after
///     this pipeline and no advisor can short-circuit it.
///     <para>
///         The pipeline is ordered by <see cref="IAdvisor.Order" /> and
///         <see cref="AdviseResult.Block" /> or <see cref="AdviseResult.Handle" /> stops the
///         remaining advisors. Neither undoes the transition: the engine has already produced the
///         snapshot by the time advisors run, so the only way to reject it is to throw, which aborts
///         before persistence and rolls back everything that joined the unit of work. External
///         writes that already completed remain.
///     </para>
/// </remarks>
public interface IFlowTransitionAdvisor : IAdvisor<FlowTransitionContext>
{
}
