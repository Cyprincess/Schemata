using Schemata.Report.Foundation;
using Schemata.Report.Scheduling.Features;
using Schemata.Report.Skeleton;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary><see cref="SchemataReportBuilder{TReport,TSnapshot,TChunk}"/> extensions for periodic scheduling.</summary>
public static class ReportSchedulingBuilderExtensions
{
    /// <summary>Adds periodic report generation through the scheduler.</summary>
    /// <typeparam name="TReport">Persisted report-definition entity type.</typeparam>
    /// <typeparam name="TSnapshot">Persisted report-snapshot entity type.</typeparam>
    /// <typeparam name="TChunk">Persisted report-snapshot chunk entity type.</typeparam>
    /// <param name="builder">The Report builder returned by <c>UseReport</c>.</param>
    /// <returns>The supplied <paramref name="builder"/>.</returns>
    public static SchemataReportBuilder<TReport, TSnapshot, TChunk> UseScheduling<TReport, TSnapshot, TChunk>(
        this SchemataReportBuilder<TReport, TSnapshot, TChunk> builder
    )
        where TReport : SchemataReport, new()
        where TSnapshot : SchemataReportSnapshot, new()
        where TChunk : SchemataReportSnapshotChunk, new() {
        builder.AddFeature<SchemataReportSchedulingFeature<TReport, TSnapshot, TChunk>>();
        return builder;
    }
}
