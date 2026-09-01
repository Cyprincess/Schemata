using Schemata.Abstractions.Advisors;

namespace Schemata.Report.Skeleton.Advisors;

/// <summary>Runs before report definition resolution; implementations return <see cref="AdviseResult.Continue" /> to continue generation, and thrown exceptions abort it.</summary>
public interface IReportGenerateAdvisor : IAdvisor<ReportGenerateContext>
{
}