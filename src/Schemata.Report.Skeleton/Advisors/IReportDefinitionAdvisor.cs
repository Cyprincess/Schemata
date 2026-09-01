using Schemata.Abstractions.Advisors;

namespace Schemata.Report.Skeleton.Advisors;

/// <summary>Runs after report definition resolution and before Insight planning; implementations return <see cref="AdviseResult.Continue" /> to continue generation, and thrown exceptions abort it.</summary>
public interface IReportDefinitionAdvisor : IAdvisor<ReportDefinitionContext>
{
}