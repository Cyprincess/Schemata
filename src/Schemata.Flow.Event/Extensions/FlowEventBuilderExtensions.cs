using Schemata.Core;
using Schemata.Flow.Event.Features;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary><see cref="SchemataBuilder"/> extensions for the Flow.Event bridge.</summary>
public static class FlowEventBuilderExtensions
{
    /// <summary>Registers <see cref="Schemata.Flow.Event.Features.SchemataFlowEventFeature"/>.</summary>
    public static SchemataBuilder UseFlowEvent(this SchemataBuilder builder) {
        builder.AddFeature<SchemataFlowEventFeature>();
        return builder;
    }
}
