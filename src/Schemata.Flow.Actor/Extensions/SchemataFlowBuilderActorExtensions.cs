using Schemata.Flow.Actor.Features;
using Schemata.Flow.Foundation.Builders;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary><see cref="SchemataFlowBuilder" /> extensions for the Flow.Actor bridge.</summary>
public static class SchemataFlowBuilderActorExtensions
{
    /// <summary>Enables the <see cref="SchemataFlowActorFeature" />.</summary>
    /// <param name="builder">The flow builder.</param>
    /// <returns>The builder for chaining.</returns>
    public static SchemataFlowBuilder UseActor(this SchemataFlowBuilder builder) {
        builder.AddFeature<SchemataFlowActorFeature>();

        return builder;
    }
}
