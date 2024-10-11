using Schemata.Entity.Cache.Advices;

// ReSharper disable once CheckNamespace
namespace Schemata.Entity.Repository;

public static class RepositoryExtensions
{
    public static IRepository SuppressQueryCache(this IRepository repository) {
        repository.AdviceContext.Set<SuppressQueryCache>(null);
        return repository;
    }

    public static IRepository<TEntity> SuppressQueryCache<TEntity>(this IRepository<TEntity> repository)
        where TEntity : class {
        repository.AdviceContext.Set<SuppressQueryCache>(null);
        return repository;
    }
}
