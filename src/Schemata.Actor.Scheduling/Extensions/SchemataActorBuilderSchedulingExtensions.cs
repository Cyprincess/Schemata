using Schemata.Actor.Foundation;
using Schemata.Actor.Scheduling.Features;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary><see cref="SchemataActorBuilder" /> extensions for the Actor.Scheduling bridge.</summary>
public static class SchemataActorBuilderSchedulingExtensions
{
    /// <summary>Enables the <see cref="SchemataActorSchedulingFeature" />.</summary>
    /// <param name="builder">The actor builder.</param>
    /// <returns>The builder for chaining.</returns>
    public static SchemataActorBuilder UseScheduling(this SchemataActorBuilder builder) {
        builder.AddFeature<SchemataActorSchedulingFeature>();

        return builder;
    }
}
