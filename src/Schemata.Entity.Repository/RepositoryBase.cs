using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Entity.Repository;

public abstract class RepositoryBase<TEntity> : IRepository<TEntity>
    where TEntity : class
{
    #region IRepository<TEntity> Members

    public virtual Expression<Func<TEntity, bool>>? Query(Expression<Func<TEntity, bool>>? predicate = null) {
        return predicate;
    }

    public abstract IAsyncEnumerable<TEntity> ListAsync(
        Expression<Func<TEntity, bool>>? predicate,
        CancellationToken                ct = default);

    public abstract Task<TEntity?> FirstOrDefaultAsync(
        Expression<Func<TEntity, bool>>? predicate,
        CancellationToken                ct = default);

    public abstract Task<TEntity?> SingleOrDefaultAsync(
        Expression<Func<TEntity, bool>>? predicate,
        CancellationToken                ct = default);

    public abstract Task<bool> AnyAsync(Expression<Func<TEntity, bool>>?   predicate, CancellationToken ct = default);
    public abstract Task<int>  CountAsync(Expression<Func<TEntity, bool>>? predicate, CancellationToken ct = default);

    public abstract Task<long> LongCountAsync(
        Expression<Func<TEntity, bool>>? predicate,
        CancellationToken                ct = default);

    public abstract Task AddAsync(TEntity entity, CancellationToken ct = default);

    public virtual Task AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken ct = default) {
        var tasks = entities.Select(e => AddAsync(e, ct)).ToArray();

        return Task.WhenAny(tasks);
    }

    public abstract Task UpdateAsync(TEntity entity, CancellationToken ct = default);

    public abstract Task RemoveAsync(TEntity entity, CancellationToken ct = default);

    public virtual Task RemoveRangeAsync(IEnumerable<TEntity> entities, CancellationToken ct = default) {
        var tasks = entities.Select(e => RemoveAsync(e, ct)).ToArray();

        return Task.WhenAny(tasks);
    }

    public abstract Task<int> CommitAsync(CancellationToken ct = default);

    #endregion
}
