using System;
using Schemata.Core;
using Schemata.Insight.Foundation;
using Schemata.Insight.Foundation.Features;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>
///     Extension methods for enabling the Insight federated query system on a
///     <see cref="SchemataBuilder" />.
/// </summary>
public static class SchemataBuilderExtensions
{
    /// <summary>
    ///     Enables the Insight query system and returns a <see cref="SchemataInsightBuilder" /> for
    ///     enabling languages, registering sources, and adding drivers.
    /// </summary>
    /// <param name="builder">The <see cref="SchemataBuilder" />.</param>
    /// <param name="configure">An optional callback configuring the Insight builder.</param>
    /// <returns>The <see cref="SchemataInsightBuilder" /> for chaining.</returns>
    public static SchemataInsightBuilder UseInsight(
        this SchemataBuilder            builder,
        Action<SchemataInsightBuilder>? configure = null
    ) {
        builder.AddFeature<SchemataInsightFeature>();

        var insight = new SchemataInsightBuilder(builder.Options, builder.Services);
        configure?.Invoke(insight);
        return insight;
    }
}
