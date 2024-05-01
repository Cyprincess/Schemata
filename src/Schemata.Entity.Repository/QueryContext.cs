using System.Linq;

namespace Schemata.Entity.Repository;

public class QueryContext<TEntity, TResult, T>(IRepository<TEntity> repository, IQueryable<TResult> query)
    where TEntity : class
{
    public IRepository<TEntity> Repository { get; private set; } = repository;

    public IQueryable<TResult> Query { get; private set; } = query;

    public T? Result { get; set; } = default;
}
