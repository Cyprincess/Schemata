using Microsoft.Extensions.DependencyInjection;

namespace Schemata.Entity.Repository;

public sealed class SchemataRepositoryBuilder(IServiceCollection services)
{
    public IServiceCollection Services => services;
}
