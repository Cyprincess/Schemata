using Schemata.Abstractions.Advisors;
using Schemata.Insight.Skeleton.Plan;
using Schemata.Insight.Skeleton.Queries;

namespace Schemata.Insight.Skeleton.Advisors;

/// <summary>
///     Runs after the plan is built and before splitting: a plan-rewrite hook. The current plan is
///     carried on the <see cref="AdviceContext" /> under <see cref="PlanNode" />; an advisor reads it
///     with <see cref="AdviceContext.Get{T}" /> and stores its rewrite with
///     <see cref="AdviceContext.Set{T}" />, so successive advisors chain and the executor consumes the
///     final rewrite.
/// </summary>
public interface IInsightPlanAdvisor : IAdvisor<QueryInsightRequest>;