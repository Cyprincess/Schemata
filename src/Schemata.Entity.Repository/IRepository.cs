using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Entity.Repository;

public interface IRepository<TEntity>
    where TEntity : class
{
    Expression<Func<TEntity, bool>>? Query(Expression<Func<TEntity, bool>>? predicate = null);

    IAsyncEnumerable<TEntity> ListAsync(Expression<Func<TEntity, bool>>? predicate, CancellationToken ct = default);

    Task<TEntity?> FirstOrDefaultAsync(Expression<Func<TEntity, bool>>?  predicate, CancellationToken ct = default);
    Task<TEntity?> SingleOrDefaultAsync(Expression<Func<TEntity, bool>>? predicate, CancellationToken ct = default);
    Task<bool>     AnyAsync(Expression<Func<TEntity, bool>>?             predicate, CancellationToken ct = default);
    Task<int>      CountAsync(Expression<Func<TEntity, bool>>?           predicate, CancellationToken ct = default);
    Task<long>     LongCountAsync(Expression<Func<TEntity, bool>>?       predicate, CancellationToken ct = default);

    Task AddAsync(TEntity                   entity,   CancellationToken ct = default);
    Task AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken ct = default);

    Task UpdateAsync(TEntity entity, CancellationToken ct = default);

    Task RemoveAsync(TEntity                   entity,   CancellationToken ct = default);
    Task RemoveRangeAsync(IEnumerable<TEntity> entities, CancellationToken ct = default);

    Task<int> CommitAsync(CancellationToken ct = default);
}
