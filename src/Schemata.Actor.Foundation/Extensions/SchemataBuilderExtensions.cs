using System;
using Schemata.Actor.Foundation;
using Schemata.Actor.Foundation.Features;
using Schemata.Core;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>
///     Provides the <c>UseActor</c> extension method on <see cref="SchemataBuilder" />.
/// </summary>
public static class SchemataBuilderExtensions
{
    /// <summary>
    ///     Enables the <see cref="SchemataActorFeature" /> and returns a
    ///     <see cref="SchemataActorBuilder" /> for registering actor types, optionally configured
    ///     inline through <paramref name="configure" />.
    /// </summary>
    /// <param name="builder">The Schemata builder.</param>
    /// <param name="configure">An optional callback that configures the actor builder.</param>
    /// <returns>The actor builder for chaining, e.g. by an actor bridge package's own <c>Use...</c> extension.</returns>
    /// <seealso cref="SchemataActorFeature" />
    public static SchemataActorBuilder UseActor(this SchemataBuilder builder, Action<SchemataActorBuilder>? configure = null) {
        builder.AddFeature<SchemataActorFeature>();

        var actor = new SchemataActorBuilder(builder.Options, builder.Services);
        configure?.Invoke(actor);

        return actor;
    }
}
