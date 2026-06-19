using Schemata.Core;
using Schemata.Flow.Foundation.Builders;
using Schemata.Flow.Scheduling.Features;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary><see cref="SchemataBuilder"/> extensions for the Flow.Scheduling bridge.</summary>
public static class FlowSchedulingBuilderExtensions
{
    /// <summary>Registers <see cref="Schemata.Flow.Scheduling.Features.SchemataFlowSchedulingFeature"/>.</summary>
    public static SchemataFlowBuilder UseScheduling(this SchemataFlowBuilder builder) {
        builder.AddFeature<SchemataFlowSchedulingFeature>();
        return builder;
    }
}
