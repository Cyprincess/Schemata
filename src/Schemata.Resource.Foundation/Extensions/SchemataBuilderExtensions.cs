using Schemata.Core;
using Schemata.Resource.Foundation;
using Schemata.Resource.Foundation.Features;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>
///     Extension methods for enabling the resource system on a <see cref="SchemataBuilder" />.
/// </summary>
public static class SchemataBuilderExtensions
{
    /// <summary>
    ///     Enables the resource system and returns a <see cref="SchemataResourceBuilder" />
    ///     for further configuration.
    /// </summary>
    /// <param name="builder">The <see cref="SchemataBuilder" />.</param>
    /// <returns>A <see cref="SchemataResourceBuilder" /> for chaining resource configuration.</returns>
    public static SchemataResourceBuilder UseResource(this SchemataBuilder builder) {
        builder.AddFeature<SchemataResourceFeature>();

        return new(builder.Options, builder.Services);
    }
}
