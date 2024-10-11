using System;
using System.Linq;

namespace Schemata.Entity.Repository;

public sealed class QueryContainer<TEntity> where TEntity : class
{
    public QueryContainer(IRepository<TEntity> repository, IQueryable<TEntity> query) {
        Repository = repository;
        Query      = query;
    }

    public IRepository<TEntity> Repository { get; private set; }

    public IQueryable<TEntity> Query { get; private set; }

    public void ApplyModification(Func<IQueryable<TEntity>, IQueryable<TEntity>> modify) {
        Query = modify(Query);
    }
}
