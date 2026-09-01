using System.Security.Claims;
using Schemata.Abstractions.Advisors;

namespace Schemata.Insight.Skeleton;

/// <summary>
///     Runs after the plan is built and before splitting: a plan-rewrite hook. The current plan is
///     carried on the <see cref="AdviceContext" /> under <see cref="PlanNode" />; an advisor reads it
///     with <see cref="AdviceContext.Get{T}" /> and stores its rewrite with
///     <see cref="AdviceContext.Set{T}" />, so successive advisors chain and the executor consumes the
///     final rewrite.
/// </summary>
public interface IInsightPlanAdvisor : IAdvisor<QueryInsightRequest>;

/// <summary>
///     Runs before each source is opened: a source-level hook that may block disallowed sources.
///     Return <see cref="AdviseResult.Block" /> or throw to block the source.
/// </summary>
public interface IInsightSourceAdvisor : IAdvisor<SourceBinding, SourceConfig, ClaimsPrincipal?>;
