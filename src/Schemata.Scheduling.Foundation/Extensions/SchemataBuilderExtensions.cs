using Schemata.Core;
using Schemata.Scheduling.Foundation.Builders;
using Schemata.Scheduling.Foundation.Features;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary><see cref="SchemataBuilder" /> extensions that activate the Scheduling feature.</summary>
public static class SchemataBuilderExtensions
{
    /// <summary>Adds <see cref="SchemataSchedulingFeature" /> and returns a <see cref="SchedulingBuilder" />.</summary>
    public static SchedulingBuilder UseScheduling(this SchemataBuilder builder) {
        builder.AddFeature<SchemataSchedulingFeature>();

        return new(builder.Services);
    }
}
