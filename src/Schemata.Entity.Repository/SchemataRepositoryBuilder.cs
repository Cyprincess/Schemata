using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Entity.Repository;

public class SchemataRepositoryBuilder(IServiceCollection services)
{
    public IServiceCollection Services { get; } = services;
}
