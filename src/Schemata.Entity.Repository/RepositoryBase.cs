using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Entity.Repository;

public abstract class RepositoryBase<TEntity> : IRepository<TEntity>
    where TEntity : class
{
    #region IRepository<TEntity> Members

    public abstract IAsyncEnumerable<TResult> ListAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default);

    public abstract Task<TResult?> FirstOrDefaultAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default);

    public abstract Task<TResult?> SingleOrDefaultAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default);

    public abstract Task<bool> AnyAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default);

    public abstract Task<int> CountAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default);

    public abstract Task<long> LongCountAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default);

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

    protected IQueryable<TResult> BuildQuery<TResult>(
        IQueryable<TEntity>                             table,
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate) {
        if (predicate != null) {
            return predicate(table);
        }

        if (typeof(TResult) == typeof(TEntity)) {
            return (IQueryable<TResult>)table;
        }

        return table.Select(e => (TResult)(object)e);
    }
}
