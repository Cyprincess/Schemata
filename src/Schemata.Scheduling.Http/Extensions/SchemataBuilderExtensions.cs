using Schemata.Scheduling.Foundation.Builders;
using Schemata.Scheduling.Http.Features;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary><see cref="SchedulingBuilder" /> extensions that activate Scheduling HTTP resources.</summary>
public static class SchemataBuilderExtensions
{
    /// <summary>Adds <see cref="SchemataSchedulingHttpFeature" />.</summary>
    public static SchedulingBuilder MapHttp(this SchedulingBuilder builder) {
        builder.AddFeature<SchemataSchedulingHttpFeature>();

        return builder;
    }
}
