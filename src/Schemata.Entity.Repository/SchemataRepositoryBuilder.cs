using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Entity.Repository;

/// <summary>
///     Fluent builder for configuring repository services. Returned by
///     <see cref="Microsoft.Extensions.DependencyInjection.ServiceCollectionExtensions.AddRepository" />
///     to enable provider and advisor registrations.
/// </summary>
public sealed class SchemataRepositoryBuilder
{
    /// <summary>
    ///     Initializes a new instance of <see cref="SchemataRepositoryBuilder" />.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    public SchemataRepositoryBuilder(IServiceCollection services) { Services = services; }

    /// <summary>
    ///     Gets the service collection being configured.
    /// </summary>
    public IServiceCollection Services { get; }
}
