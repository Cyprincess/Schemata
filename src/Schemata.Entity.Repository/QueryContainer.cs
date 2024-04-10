using System;
using System.Linq;

namespace Schemata.Entity.Repository;

public sealed class QueryContainer<TEntity>
{
    public QueryContainer(IQueryable<TEntity> query) {
        Query = query;
    }

    public IQueryable<TEntity> Query { get; private set; }

    public void ApplyModification(Func<IQueryable<TEntity>, IQueryable<TEntity>> modify) {
        Query = modify(Query);
    }
}
