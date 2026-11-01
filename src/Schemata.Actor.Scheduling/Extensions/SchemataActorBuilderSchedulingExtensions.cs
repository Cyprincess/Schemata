using Schemata.Actor.Foundation;
using Schemata.Actor.Scheduling.Features;
using Schemata.Scheduling.Skeleton.Entities;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary><see cref="SchemataActorBuilder" /> extensions for the Actor.Scheduling bridge.</summary>
public static class SchemataActorBuilderSchedulingExtensions
{
    /// <summary>Enables the default <see cref="SchemataActorSchedulingFeature{TJob,TExecution}" />.</summary>
    /// <param name="builder">The actor builder.</param>
    /// <returns>The builder for chaining.</returns>
    public static SchemataActorBuilder UseScheduling(this SchemataActorBuilder builder) {
        return builder.UseScheduling<SchemataJob, SchemataJobExecution>();
    }

    /// <summary>Enables <see cref="SchemataActorSchedulingFeature{TJob,TExecution}" />.</summary>
    /// <param name="builder">The actor builder.</param>
    /// <returns>The builder for chaining.</returns>
    public static SchemataActorBuilder UseScheduling<TJob, TExecution>(this SchemataActorBuilder builder)
        where TJob : SchemataJob, new()
        where TExecution : SchemataJobExecution, new() {
        builder.AddFeature<SchemataActorSchedulingFeature<TJob, TExecution>>();

        return builder;
    }
}
