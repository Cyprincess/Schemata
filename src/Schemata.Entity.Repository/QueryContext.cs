using System.Linq;

namespace Schemata.Entity.Repository;

public class QueryContext<TEntity, TResult, T> where TEntity : class
{
    public QueryContext(IRepository<TEntity> repository, IQueryable<TResult> query) {
        Repository = repository;
        Query      = query;
    }

    public IRepository<TEntity> Repository { get; private set; }

    public IQueryable<TResult> Query { get; private set; }

    public T? Result { get; set; } = default;
}
