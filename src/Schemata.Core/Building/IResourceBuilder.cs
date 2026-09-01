using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Core.Building;

/// <summary>A builder that contributes resources to the shared resource registry.</summary>
public interface IResourceBuilder
{
    /// <summary>The Schemata options bag carrying per-domain keys.</summary>
    SchemataOptions Schemata { get; }

    /// <summary>The service collection receiving feature and advisor registrations.</summary>
    IServiceCollection Services { get; }
}
