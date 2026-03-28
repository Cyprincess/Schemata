using Schemata.Core;
using Schemata.Mapping.Foundation;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>
///     Extension methods for initializing the mapping subsystem on <see cref="SchemataBuilder" />.
/// </summary>
public static class SchemataBuilderExtensions
{
    /// <summary>
    ///     Begins configuring the mapping subsystem, returning a builder for selecting the mapping engine.
    /// </summary>
    /// <param name="builder">The Schemata builder.</param>
    /// <returns>A <see cref="SchemataMappingBuilder" /> for further configuration.</returns>
    public static SchemataMappingBuilder UseMapping(this SchemataBuilder builder) {
        return new(builder.Options, builder.Services);
    }
}
