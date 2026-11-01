using Schemata.Push.Actor.Features;
using Schemata.Push.Foundation.Builders;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>Schemata push builder extensions for the Push.Actor bridge.</summary>
public static class SchemataPushBuilderActorExtensions
{
    /// <summary>Enables the <see cref="SchemataPushActorFeature" />.</summary>
    /// <param name="builder">The push builder returned by <c>UsePush</c>.</param>
    /// <returns>The builder for chaining.</returns>
    public static SchemataPushBuilder UseActor(this SchemataPushBuilder builder) {
        builder.AddFeature<SchemataPushActorFeature>();

        return builder;
    }
}
