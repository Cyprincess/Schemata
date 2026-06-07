using Schemata.Core;
using Schemata.Flow.Scheduling.Features;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary><see cref="SchemataBuilder"/> extensions for the Flow.Scheduling bridge.</summary>
public static class FlowSchedulingBuilderExtensions
{
    /// <summary>Registers <see cref="Schemata.Flow.Scheduling.Features.SchemataFlowSchedulingFeature"/>.</summary>
    public static SchemataBuilder UseFlowScheduling(this SchemataBuilder builder) {
        builder.AddFeature<SchemataFlowSchedulingFeature>();
        return builder;
    }
}
