using System;
using Schemata.Core;
using Schemata.Insight.Foundation;
using Schemata.Insight.Foundation.Features;
using Schemata.Insight.Skeleton.Entities;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>
///     Extension methods for enabling the Insight federated query system on a
///     <see cref="SchemataBuilder" />.
/// </summary>
public static class SchemataBuilderExtensions
{
    /// <summary>
    ///     Enables the Insight query system closed on <see cref="SchemataInsightSource" /> and returns
    ///     a <see cref="SchemataInsightBuilder" /> for enabling languages, registering sources, and
    ///     adding drivers.
    /// </summary>
    /// <param name="builder">The <see cref="SchemataBuilder" />.</param>
    /// <param name="configure">An optional callback configuring the Insight builder.</param>
    /// <returns>The <see cref="SchemataInsightBuilder" /> for chaining.</returns>
    public static SchemataInsightBuilder UseInsight(
        this SchemataBuilder            builder,
        Action<SchemataInsightBuilder>? configure = null
    ) {
        return builder.UseInsight<SchemataInsightSource>(configure);
    }

    /// <summary>
    ///     Enables the Insight query system closed on <typeparamref name="TInsightSource" /> and
    ///     returns a <see cref="SchemataInsightBuilder" /> for enabling languages, registering
    ///     sources, and adding drivers. The database catalog resolves only the selected source entity.
    /// </summary>
    /// <typeparam name="TInsightSource">The persisted source entity type.</typeparam>
    /// <param name="builder">The <see cref="SchemataBuilder" />.</param>
    /// <param name="configure">An optional callback configuring the Insight builder.</param>
    /// <returns>The <see cref="SchemataInsightBuilder" /> for chaining.</returns>
    public static SchemataInsightBuilder UseInsight<TInsightSource>(
        this SchemataBuilder            builder,
        Action<SchemataInsightBuilder>? configure = null
    )
        where TInsightSource : SchemataInsightSource, new() {
        builder.AddFeature<SchemataInsightFeature<TInsightSource>>();

        var insight = new SchemataInsightBuilder(builder.Options, builder.Services, typeof(TInsightSource));
        configure?.Invoke(insight);
        return insight;
    }
}
