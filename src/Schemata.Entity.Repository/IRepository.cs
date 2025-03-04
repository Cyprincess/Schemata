using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Entity.Repository;

public interface IRepository
{
    IAsyncEnumerable<object> ListAsync<T>(Expression<Func<T, bool>>? predicate, CancellationToken ct = default);

    ValueTask<object?> FirstOrDefaultAsync<T>(Expression<Func<T, bool>>?  predicate, CancellationToken ct = default);
    ValueTask<object?> SingleOrDefaultAsync<T>(Expression<Func<T, bool>>? predicate, CancellationToken ct = default);
    ValueTask<bool>    AnyAsync<T>(Expression<Func<T, bool>>?             predicate, CancellationToken ct = default);
    ValueTask<int>     CountAsync<T>(Expression<Func<T, bool>>?           predicate, CancellationToken ct = default);
    ValueTask<long>    LongCountAsync<T>(Expression<Func<T, bool>>?       predicate, CancellationToken ct = default);

    Task AddAsync(object    entity, CancellationToken ct = default);
    Task UpdateAsync(object entity, CancellationToken ct = default);
    Task RemoveAsync(object entity, CancellationToken ct = default);

    ValueTask<int> CommitAsync(CancellationToken ct = default);
}
