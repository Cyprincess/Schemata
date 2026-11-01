using Schemata.Report.Scheduling.Features;
using Schemata.Report.Skeleton.Entities;
using Schemata.Scheduling.Skeleton.Entities;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary><see cref="Schemata.Report.Foundation.SchemataReportBuilder{TReport,TSnapshot,TChunk}"/> extensions for periodic scheduling.</summary>
public static class ReportSchedulingBuilderExtensions
{
    /// <summary>Adds periodic report generation through the scheduler.</summary>
    /// <typeparam name="TReport">Persisted report-definition entity type.</typeparam>
    /// <typeparam name="TSnapshot">Persisted report-snapshot entity type.</typeparam>
    /// <typeparam name="TChunk">Persisted report-snapshot chunk entity type.</typeparam>
    /// <param name="builder">The Report builder returned by <c>UseReport</c>.</param>
    /// <returns>The supplied <paramref name="builder"/>.</returns>
    public static Schemata.Report.Foundation.SchemataReportBuilder<TReport, TSnapshot, TChunk> UseScheduling<TReport, TSnapshot, TChunk>(
        this Schemata.Report.Foundation.SchemataReportBuilder<TReport, TSnapshot, TChunk> builder
    )
        where TReport : SchemataReport, new()
        where TSnapshot : SchemataReportSnapshot, new()
        where TChunk : SchemataReportSnapshotChunk, new() {
        return builder.UseScheduling<TReport, TSnapshot, TChunk, SchemataJob, SchemataJobExecution>();
    }

    /// <summary>Adds periodic report generation through the scheduler closed on the host job entities.</summary>
    /// <typeparam name="TReport">Persisted report-definition entity type.</typeparam>
    /// <typeparam name="TSnapshot">Persisted report-snapshot entity type.</typeparam>
    /// <typeparam name="TChunk">Persisted report-snapshot chunk entity type.</typeparam>
    /// <typeparam name="TJob">The persisted job entity type registered by the host.</typeparam>
    /// <typeparam name="TExecution">The persisted execution entity type registered by the host.</typeparam>
    /// <param name="builder">The Report builder returned by <c>UseReport</c>.</param>
    /// <returns>The supplied <paramref name="builder"/>.</returns>
    public static Schemata.Report.Foundation.SchemataReportBuilder<TReport, TSnapshot, TChunk> UseScheduling<TReport, TSnapshot, TChunk, TJob, TExecution>(
        this Schemata.Report.Foundation.SchemataReportBuilder<TReport, TSnapshot, TChunk> builder
    )
        where TReport : SchemataReport, new()
        where TSnapshot : SchemataReportSnapshot, new()
        where TChunk : SchemataReportSnapshotChunk, new()
        where TJob : SchemataJob, new()
        where TExecution : SchemataJobExecution, new() {
        builder.AddFeature<SchemataReportSchedulingFeature<TReport, TSnapshot, TChunk, TJob, TExecution>>();
        return builder;
    }
}
