using Microsoft.Extensions.DependencyInjection;
using Schemata.Core;

namespace Schemata.Mapping.Foundation;

/// <summary>
///     Fluent builder for configuring the mapping subsystem and selecting a mapping engine.
/// </summary>
public sealed class SchemataMappingBuilder
{
    /// <summary>
    ///     Initializes a new instance of the mapping builder.
    /// </summary>
    /// <param name="schemata">The Schemata options for feature registration.</param>
    /// <param name="services">The service collection for registering mapping services.</param>
    public SchemataMappingBuilder(SchemataOptions schemata, IServiceCollection services) {
        Schemata = schemata;
        Services = services;
    }

    /// <summary>
    ///     The Schemata options used for feature registration.
    /// </summary>
    public SchemataOptions Schemata { get; }

    /// <summary>
    ///     The service collection for registering mapping services.
    /// </summary>
    public IServiceCollection Services { get; }
}
