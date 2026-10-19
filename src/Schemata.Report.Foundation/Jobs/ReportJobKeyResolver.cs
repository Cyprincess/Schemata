using System;
using Schemata.Report.Skeleton.Entities;
using Schemata.Scheduling.Skeleton;

namespace Schemata.Report.Foundation.Jobs;

/// <summary>Stable scheduler keys shared by every closed <see cref="ReportJobKeyResolver{TReport, TSnapshot, TChunk}" />.</summary>
public static class ReportJobKeyResolver
{
    /// <summary>Stable report-generation scheduler key.</summary>
    public const string Key = "schemata.report.generate";

    /// <summary>
    ///     The scheduler slot key of a report's schedule. The canonical name is unique among active
    ///     reports; a configuration-only definition falls back to its public name.
    /// </summary>
    /// <param name="canonicalName">The persisted canonical name, or <see langword="null" />.</param>
    /// <param name="name">The public report name, or <see langword="null" />.</param>
    /// <returns>The slot key.</returns>
    public static string Slot(string? canonicalName, string? name) => $"report:{canonicalName ?? name}";

    /// <summary>
    ///     The scheduler slot key of <paramref name="report" />.
    /// </summary>
    /// <param name="report">The report definition.</param>
    /// <returns>The slot key.</returns>
    public static string Slot(SchemataReport report) => Slot(report.CanonicalName, report.Name);
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
    public Type? ResolveType(string key) {
        return string.Equals(key, ReportJobKeyResolver.Key, StringComparison.Ordinal)
            ? typeof(ReportGenerationJob<TReport, TSnapshot, TChunk>)
            : null;
    }

    public string? ResolveKey(Type jobType) {
        return jobType == typeof(ReportGenerationJob<TReport, TSnapshot, TChunk>) ? ReportJobKeyResolver.Key : null;
    }
}
