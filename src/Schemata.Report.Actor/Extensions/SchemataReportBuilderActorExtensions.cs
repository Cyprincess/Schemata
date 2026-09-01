using Schemata.Report.Actor.Features;
using Schemata.Report.Foundation;
using Schemata.Report.Skeleton;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary><see cref="SchemataReportBuilder{TReport,TSnapshot,TChunk}" /> extensions for the Report.Actor bridge.</summary>
public static class SchemataReportBuilderActorExtensions
{
    /// <summary>Enables the <see cref="SchemataReportActorFeature{TReport,TSnapshot,TChunk}" />.</summary>
    /// <typeparam name="TReport">Persisted report-definition entity type.</typeparam>
    /// <typeparam name="TSnapshot">Persisted report-snapshot entity type.</typeparam>
    /// <typeparam name="TChunk">Persisted report-snapshot chunk entity type.</typeparam>
    /// <param name="builder">The report builder.</param>
    /// <returns>The builder for chaining.</returns>
    public static SchemataReportBuilder<TReport, TSnapshot, TChunk> UseActor<TReport, TSnapshot, TChunk>(
        this SchemataReportBuilder<TReport, TSnapshot, TChunk> builder
    )
        where TReport : SchemataReport, new()
        where TSnapshot : SchemataReportSnapshot, new()
        where TChunk : SchemataReportSnapshotChunk, new() {
        builder.AddFeature<SchemataReportActorFeature<TReport, TSnapshot, TChunk>>();

        return builder;
    }
}