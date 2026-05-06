using System;
using Schemata.Core;
using Schemata.Flow.Foundation.Builders;
using Schemata.Flow.Foundation.Features;
using Schemata.Flow.Skeleton;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>
///     Provides the <c>UseFlow</c> extension method on <see cref="SchemataBuilder" />.
/// </summary>
public static class FlowBuilderExtensions
{
    /// <summary>
    ///     Registers the flow engine and supporting services into the DI container.
    ///     Enables the <see cref="SchemataFlowFeature" />, which wires the flow
    ///     middleware and endpoint pipeline.
    /// </summary>
    /// <param name="builder">The Schemata builder.</param>
    /// <param name="configure">
    ///     An optional callback to pre-register process definitions via <see cref="FlowBuilder" />.
    /// </param>
    /// <returns>The current <see cref="SchemataBuilder" /> for chaining.</returns>
    /// <seealso cref="SchemataFlowFeature" />
    public static SchemataBuilder UseFlow(this SchemataBuilder builder, Action<FlowBuilder>? configure = null) {
        configure ??= _ => { };
        builder.Configure(configure);

        builder.AddFeature<SchemataFlowFeature>();

        return builder;
    }
}
