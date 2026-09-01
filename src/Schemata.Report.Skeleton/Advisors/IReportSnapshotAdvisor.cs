using Schemata.Abstractions.Advisors;

namespace Schemata.Report.Skeleton.Advisors;

/// <summary>Runs after report materialization and before persisted snapshot finalization; implementations return <see cref="AdviseResult.Continue" /> to continue generation, and thrown exceptions abort it.</summary>
public interface IReportSnapshotAdvisor : IAdvisor<ReportSnapshotContext>
{
}