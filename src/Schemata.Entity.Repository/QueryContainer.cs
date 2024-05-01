using System;
using System.Linq;

namespace Schemata.Entity.Repository;

public sealed class QueryContainer<TEntity>(IRepository<TEntity> repository, IQueryable<TEntity> query)
    where TEntity : class
{
    public IRepository<TEntity> Repository { get; private set; } = repository;

    public IQueryable<TEntity> Query { get; private set; } = query;

    public void ApplyModification(Func<IQueryable<TEntity>, IQueryable<TEntity>> modify) {
        Query = modify(Query);
    }
}
