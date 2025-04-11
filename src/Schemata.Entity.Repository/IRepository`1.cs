using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advices;

namespace Schemata.Entity.Repository;

public interface IRepository<TEntity> where TEntity : class
{
    AdviceContext AdviceContext { get; }

    IAsyncEnumerable<TEntity> AsAsyncEnumerable();

    IQueryable<TEntity> AsQueryable();

    IAsyncEnumerable<TResult> ListAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default);

    IAsyncEnumerable<TResult> SearchAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default);

    ValueTask<TEntity?> GetAsync(TEntity entity, CancellationToken ct = default);

    ValueTask<TResult?> GetAsync<TResult>(TEntity entity, CancellationToken ct = default);

    ValueTask<TEntity?> FindAsync(object[] keys, CancellationToken ct = default);

    ValueTask<TResult?> FindAsync<TResult>(object[] keys, CancellationToken ct = default);

    ValueTask<TResult?> FirstOrDefaultAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default);

    ValueTask<TResult?> SingleOrDefaultAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default);

    ValueTask<bool> AnyAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default);

    ValueTask<int> CountAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default);

    ValueTask<long> LongCountAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default);

    Task AddAsync(TEntity                   entity,   CancellationToken ct = default);
    Task AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken ct = default);

    Task UpdateAsync(TEntity entity, CancellationToken ct = default);

    Task RemoveAsync(TEntity                   entity,   CancellationToken ct = default);
    Task RemoveRangeAsync(IEnumerable<TEntity> entities, CancellationToken ct = default);

    ValueTask<int> CommitAsync(CancellationToken ct = default);

    void Detach(TEntity entity);

    IRepository<TEntity> Once();
    IRepository<TEntity> SuppressAddValidation();
    IRepository<TEntity> SuppressUpdateValidation();
    IRepository<TEntity> SuppressUpdateConcurrency();
    IRepository<TEntity> SuppressQuerySoftDelete();
    IRepository<TEntity> SuppressRemoveSoftDelete();
}
