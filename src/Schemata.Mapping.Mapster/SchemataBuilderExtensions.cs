using Schemata.Core;
using Schemata.Mapping.Foundation;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>
/// Extension methods for enabling Mapster on <see cref="SchemataBuilder"/>.
/// </summary>
public static class SchemataBuilderExtensions
{
    /// <summary>
    /// Enables the mapping subsystem with Mapster as the mapping engine.
    /// </summary>
    /// <param name="builder">The Schemata builder.</param>
    /// <returns>A <see cref="SchemataMappingBuilder"/> for further configuration.</returns>
    public static SchemataMappingBuilder UseMapster(this SchemataBuilder builder) {
        return builder.UseMapping()
                      .UseMapster();
    }
}
