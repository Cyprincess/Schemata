using System;
using Schemata.Core;
using Schemata.Report.Foundation;
using Schemata.Report.Foundation.Features;
using Schemata.Report.Skeleton;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>Extension methods for configuring the Report feature.</summary>
public static class SchemataBuilderExtensions
{
    /// <summary>Adds the Report feature using the default persisted entity types.</summary>
    /// <param name="builder">The Schemata host builder.</param>
    /// <param name="configure">Optional callback that configures report limits and defaults.</param>
    /// <returns>A Report builder for additional configuration.</returns>
    /// <remarks>
    ///     Hosts typically enable expression languages once through <c>UseInsight()</c>. Insight execution also
    ///     requires those language services because <c>InsightPlanBuilder</c> resolves keyed compilers.
    /// </remarks>
    public static SchemataReportBuilder<SchemataReport, SchemataReportSnapshot, SchemataReportSnapshotChunk> UseReport(
        this SchemataBuilder             builder,
        Action<SchemataReportOptions>? configure = null
    ) {
        return builder.UseReport<SchemataReport, SchemataReportSnapshot, SchemataReportSnapshotChunk>(configure);
    }

    /// <summary>Adds the Report feature using host-defined derived entity types.</summary>
    /// <typeparam name="TReport">Persisted report-definition entity type.</typeparam>
    /// <typeparam name="TSnapshot">Persisted report-snapshot entity type.</typeparam>
    /// <typeparam name="TChunk">Persisted report-snapshot chunk entity type.</typeparam>
    /// <param name="builder">The Schemata host builder.</param>
    /// <param name="configure">Optional callback that configures report limits and defaults.</param>
    /// <returns>A Report builder for additional configuration.</returns>
    /// <remarks>
    ///     Hosts typically enable expression languages once through <c>UseInsight()</c>. Insight execution also
    ///     requires those language services because <c>InsightPlanBuilder</c> resolves keyed compilers.
    /// </remarks>
    public static SchemataReportBuilder<TReport, TSnapshot, TChunk> UseReport<TReport, TSnapshot, TChunk>(
        this SchemataBuilder             builder,
        Action<SchemataReportOptions>? configure = null
    )
        where TReport : SchemataReport, new()
        where TSnapshot : SchemataReportSnapshot, new()
        where TChunk : SchemataReportSnapshotChunk, new() {
        configure ??= _ => { };
        builder.Configure(configure);
        builder.AddFeature<SchemataReportFeature<TReport, TSnapshot, TChunk>>();

        return new(builder.Options, builder.Services);
    }
}
