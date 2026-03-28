using Schemata.Core;
using Schemata.Resource.Foundation;
using Schemata.Resource.Foundation.Features;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>
///     Extension methods for configuring resource services on <see cref="SchemataBuilder" />.
/// </summary>
public static class SchemataBuilderExtensions
{
    /// <summary>
    ///     Enables the resource system and returns a builder for further configuration.
    /// </summary>
    /// <param name="builder">The Schemata builder.</param>
    /// <returns>A resource builder for configuring resources, authorization, and options.</returns>
    public static SchemataResourceBuilder UseResource(this SchemataBuilder builder) {
        builder.AddFeature<SchemataResourceFeature>();

        return new(builder.Options, builder.Services);
    }
}
