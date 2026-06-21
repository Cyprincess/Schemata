using Schemata.Insight.Foundation;
using Schemata.Insight.Http.Features;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>
///     Extension methods for enabling HTTP transport on the Insight builder.
/// </summary>
public static class SchemataInsightBuilderExtensions
{
    /// <summary>
    ///     Exposes the Insight query endpoint over HTTP and returns the same builder so registration
    ///     chains.
    /// </summary>
    /// <param name="builder">The Insight builder.</param>
    /// <returns>The Insight builder for chaining.</returns>
    public static SchemataInsightBuilder MapHttp(this SchemataInsightBuilder builder) {
        builder.AddFeature<SchemataInsightHttpFeature>();

        return builder;
    }
}
