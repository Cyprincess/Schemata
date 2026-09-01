using Schemata.Core;
using Schemata.Push.Actor.Features;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>Schemata builder extensions for the Push.Actor bridge.</summary>
public static class SchemataPushBuilderActorExtensions
{
    /// <summary>Enables the <see cref="SchemataPushActorFeature" />.</summary>
    /// <param name="builder">The Schemata builder.</param>
    /// <returns>The builder for chaining.</returns>
    public static SchemataBuilder UsePushActor(this SchemataBuilder builder) {
        builder.AddFeature<SchemataPushActorFeature>();

        return builder;
    }
}