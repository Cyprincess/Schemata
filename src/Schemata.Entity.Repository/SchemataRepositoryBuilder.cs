using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Entity.Repository;

/// <summary>
///     Builder for configuring repository services, returned by <see cref="Microsoft.Extensions.DependencyInjection.ServiceCollectionExtensions.AddRepository" /> to enable fluent chaining of provider and advisor registrations.
/// </summary>
public sealed class SchemataRepositoryBuilder
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="SchemataRepositoryBuilder" /> class.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    public SchemataRepositoryBuilder(IServiceCollection services) { Services = services; }

    /// <summary>
    ///     Gets the service collection being configured.
    /// </summary>
    public IServiceCollection Services { get; }
}
