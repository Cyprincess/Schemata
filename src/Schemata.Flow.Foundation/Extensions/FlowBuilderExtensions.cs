using Schemata.Core;
using Schemata.Flow.Foundation.Builders;
using Schemata.Flow.Foundation.Features;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>
///     Provides the <c>UseFlow</c> extension method on <see cref="SchemataBuilder" />.
/// </summary>
public static class FlowBuilderExtensions
{
    /// <summary>
    ///     Enables the <see cref="SchemataFlowFeature" /> and returns a
    ///     <see cref="SchemataFlowBuilder" /> for registering process definitions.
    /// </summary>
    /// <param name="builder">The Schemata builder.</param>
    /// <returns>The flow builder for chaining.</returns>
    /// <seealso cref="SchemataFlowFeature" />
    public static SchemataFlowBuilder UseFlow(this SchemataBuilder builder) {
        builder.AddFeature<SchemataFlowFeature>();

        return new(builder.Options, builder.Services);
    }
}
