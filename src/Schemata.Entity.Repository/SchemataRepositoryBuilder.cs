using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Entity.Repository;

public sealed class SchemataRepositoryBuilder
{
    public SchemataRepositoryBuilder(IServiceCollection services) {
        Services = services;
    }

    public IServiceCollection Services { get; }
}
