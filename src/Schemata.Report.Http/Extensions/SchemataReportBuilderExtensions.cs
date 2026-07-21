using Schemata.Report.Foundation;
using Schemata.Report.Http.Features;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>Extensions that compose Report resources with HTTP endpoints.</summary>
public static class SchemataReportBuilderExtensions
{
    /// <summary>Adds <see cref="SchemataReportHttpFeature" /> and returns the same Report builder.</summary>
    /// <typeparam name="TReport">The report-definition entity type.</typeparam>
    /// <typeparam name="TSnapshot">The report snapshot entity type.</typeparam>
    /// <typeparam name="TChunk">The report snapshot chunk entity type.</typeparam>
    /// <param name="builder">The Report builder.</param>
    /// <returns>The builder for chaining.</returns>
    public static SchemataReportBuilder<TReport, TSnapshot, TChunk> MapHttp<TReport, TSnapshot, TChunk>(
        this SchemataReportBuilder<TReport, TSnapshot, TChunk> builder
    ) {
        builder.AddFeature<SchemataReportHttpFeature>();

        return builder;
    }
}
