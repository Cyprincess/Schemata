using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Entity.Repository;
using Schemata.Push.Skeleton.Entities;

namespace Schemata.Push.Tests;

/// <summary>
///     In-memory <see cref="IRepository{TEntity}" /> test double backing
///     <see cref="SchemataPushSubscription" /> rows with a <see cref="List{T}" />. Query predicates
///     run against the in-memory store via LINQ. Only the methods the manager uses are implemented;
///     the rest throw to surface accidental dependencies. Test-only.
/// </summary>
public sealed class FakePushRepository : IRepository<SchemataPushSubscription>
{
    private readonly List<SchemataPushSubscription> _store = [];

    public int Commits { get; private set; }

    public IReadOnlyList<SchemataPushSubscription> Store => _store;

    public AdviceContext AdviceContext => throw new NotSupportedException();

    public void Seed(params SchemataPushSubscription[] rows) { _store.AddRange(rows); }

    #region IRepository<SchemataPushSubscription> Members

    public async IAsyncEnumerable<TResult> ListAsync<TResult>(
        Func<IQueryable<SchemataPushSubscription>, IQueryable<TResult>>? predicate,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken ct = default
    ) {
        var query = predicate is null
            ? _store.AsQueryable().OfType<TResult>()
            : predicate(_store.AsQueryable());

        foreach (var row in query) {
            ct.ThrowIfCancellationRequested();
            yield return row;
            await Task.CompletedTask;
        }
    }

    public ValueTask<TResult?> FirstOrDefaultAsync<TResult>(
        Func<IQueryable<SchemataPushSubscription>, IQueryable<TResult>>? predicate,
        CancellationToken                                               ct = default
    ) {
        var query = predicate is null ? _store.AsQueryable().OfType<TResult>() : predicate(_store.AsQueryable());
        return new(query.FirstOrDefault());
    }

    public ValueTask<TResult?> SingleOrDefaultAsync<TResult>(
        Func<IQueryable<SchemataPushSubscription>, IQueryable<TResult>>? predicate,
        CancellationToken                                               ct = default
    ) {
        var query = predicate is null ? _store.AsQueryable().OfType<TResult>() : predicate(_store.AsQueryable());
        return new(query.SingleOrDefault());
    }

    public ValueTask<bool> AnyAsync<TResult>(
        Func<IQueryable<SchemataPushSubscription>, IQueryable<TResult>>? predicate,
        CancellationToken                                               ct = default
    ) {
        var query = predicate is null ? _store.AsQueryable().OfType<TResult>() : predicate(_store.AsQueryable());
        return new(query.Any());
    }

    public Task AddAsync(SchemataPushSubscription entity, CancellationToken ct = default) {
        _store.Add(entity);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(SchemataPushSubscription entity, CancellationToken ct = default) {
        _store.Remove(entity);
        return Task.CompletedTask;
    }

    public Task CommitAsync(CancellationToken ct = default) {
        Commits++;
        return Task.CompletedTask;
    }

    public ValueTask<SchemataPushSubscription?> GetAsync(SchemataPushSubscription? entity, CancellationToken ct = default) {
        throw new NotSupportedException();
    }

    public ValueTask<TResult?> GetAsync<TResult>(SchemataPushSubscription? entity, CancellationToken ct = default) {
        throw new NotSupportedException();
    }

    public ValueTask<SchemataPushSubscription?> FindAsync(object[] keys, CancellationToken ct = default) {
        throw new NotSupportedException();
    }

    public ValueTask<TResult?> FindAsync<TResult>(object[] keys, CancellationToken ct = default) {
        throw new NotSupportedException();
    }

    public ValueTask<int> CountAsync<TResult>(
        Func<IQueryable<SchemataPushSubscription>, IQueryable<TResult>>? predicate,
        CancellationToken                                               ct = default
    ) {
        throw new NotSupportedException();
    }

    public ValueTask<long> LongCountAsync<TResult>(
        Func<IQueryable<SchemataPushSubscription>, IQueryable<TResult>>? predicate,
        CancellationToken                                               ct = default
    ) {
        throw new NotSupportedException();
    }

    public Task AddRangeAsync(IEnumerable<SchemataPushSubscription> entities, CancellationToken ct = default) {
        throw new NotSupportedException();
    }

    public Task UpdateAsync(SchemataPushSubscription entity, CancellationToken ct = default) {
        throw new NotSupportedException();
    }

    public Task RemoveRangeAsync(IEnumerable<SchemataPushSubscription> entities, CancellationToken ct = default) {
        throw new NotSupportedException();
    }

    public IUnitOfWork Begin() { throw new NotSupportedException(); }

    public void Join(IUnitOfWork uow) { throw new NotSupportedException(); }

    public IDisposable SuppressAddValidation() { throw new NotSupportedException(); }

    public IDisposable SuppressUpdateValidation() { throw new NotSupportedException(); }

    public IDisposable SuppressQuerySoftDelete() { throw new NotSupportedException(); }

    public IDisposable SuppressSoftDelete() { throw new NotSupportedException(); }

    public IDisposable SuppressTimestamp() { throw new NotSupportedException(); }

    public void Dispose() { }

    public ValueTask DisposeAsync() { return ValueTask.CompletedTask; }

    #endregion
}
