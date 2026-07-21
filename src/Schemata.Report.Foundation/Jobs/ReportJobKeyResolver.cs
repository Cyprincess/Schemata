using System;
using Schemata.Report.Skeleton;
using Schemata.Scheduling.Skeleton;

namespace Schemata.Report.Foundation;

/// <summary>Stable scheduler key shared by every closed <see cref="ReportJobKeyResolver{TReport, TSnapshot, TChunk}" />.</summary>
public static class ReportJobKeyResolver
{
    /// <summary>Stable report-generation scheduler key.</summary>
    public const string Key = "schemata.report.generate";
}

/// <summary>Maps the stable report-generation scheduler key to the configured closed job type.</summary>
/// <typeparam name="TReport">Persisted report-definition entity type.</typeparam>
/// <typeparam name="TSnapshot">Persisted snapshot-header entity type.</typeparam>
/// <typeparam name="TChunk">Persisted snapshot-chunk entity type.</typeparam>
public sealed class ReportJobKeyResolver<TReport, TSnapshot, TChunk> : IScheduledJobKeyResolver
    where TReport : SchemataReport, new()
    where TSnapshot : SchemataReportSnapshot, new()
    where TChunk : SchemataReportSnapshotChunk, new()
{
    /// <inheritdoc />
    public Type? ResolveType(string key) {
        return string.Equals(key, ReportJobKeyResolver.Key, StringComparison.Ordinal)
            ? typeof(ReportGenerationJob<TReport, TSnapshot, TChunk>)
            : null;
    }

    /// <inheritdoc />
    public string? ResolveKey(Type jobType) {
        return jobType == typeof(ReportGenerationJob<TReport, TSnapshot, TChunk>) ? ReportJobKeyResolver.Key : null;
    }
}
