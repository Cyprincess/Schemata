using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Entity.Repository;

namespace Schemata.Report.Tests;

internal sealed class ReportTestRepository<TEntity>(
    List<TEntity>    records,
    Action?          onCommit = null,
    Action<TEntity>? onUpdate = null
) : IRepository<TEntity>
    , IReportTestUnitOfWorkParticipant
    where TEntity : class
{
    private readonly List<TEntity> _pending = [];
    private readonly List<TEntity> _removed = [];
    private bool _committed;
    private ReportTestUnitOfWork? _unit;

    public AdviceContext AdviceContext { get; } = new(new ReportTestServiceProvider());

    public IUnitOfWork Begin() {
        var unit = new ReportTestUnitOfWork();
        Join(unit);
        return unit;
    }

    public void Join(IUnitOfWork uow) {
        _unit = uow as ReportTestUnitOfWork
                ?? throw new NotSupportedException();
        _unit.Join(this);
    }

    public Task CommitAsync(CancellationToken ct = default) {
        EnsureOpen();
        if (_unit is not null) {
            return _unit.CommitAsync(ct);
        }

        Commit();
        return Task.CompletedTask;
    }

    public IDisposable SuppressAddValidation() => ReportTestDisposable.Instance;

    public IDisposable SuppressUpdateValidation() => ReportTestDisposable.Instance;

    public IDisposable SuppressQuerySoftDelete() => ReportTestDisposable.Instance;

    public IDisposable SuppressSoftDelete() => ReportTestDisposable.Instance;

    public IDisposable SuppressTimestamp() => ReportTestDisposable.Instance;

    public IAsyncEnumerable<TResult> ListAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default
    ) {
        var query = predicate is null
            ? records.OfType<TResult>().AsQueryable()
            : predicate(records.AsQueryable());
        return ReportTestRows.ToAsync(query);
    }

    public ValueTask<TEntity?> GetAsync(TEntity? entity, CancellationToken ct = default) {
        return ValueTask.FromResult(entity is not null && records.Contains(entity) ? entity : null);
    }

    public ValueTask<TResult?> GetAsync<TResult>(TEntity? entity, CancellationToken ct = default) {
        return ValueTask.FromResult(entity is TResult match && records.Contains(entity) ? match : default);
    }

    public ValueTask<TEntity?> FindAsync(object[] keys, CancellationToken ct = default) => ValueTask.FromResult<TEntity?>(null);

    public ValueTask<TResult?> FindAsync<TResult>(object[] keys, CancellationToken ct = default) => ValueTask.FromResult<TResult?>(default);

    public ValueTask<TResult?> FirstOrDefaultAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default
    ) {
        var query = predicate is null
            ? records.OfType<TResult>().AsQueryable()
            : predicate(records.AsQueryable());
        return ValueTask.FromResult(query.FirstOrDefault());
    }

    public ValueTask<TResult?> SingleOrDefaultAsync<TResult>(
        Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate,
        CancellationToken                               ct = default
    ) {
        var query = predicate is null
            ? records.OfType<TResult>().AsQueryable()
            : predicate(records.AsQueryable());
        return ValueTask.FromResult(query.SingleOrDefault());
    }

    public ValueTask<bool> AnyAsync<TResult>(Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate, CancellationToken ct = default) {
        return ValueTask.FromResult(List(predicate).Any());
    }

    public ValueTask<int> CountAsync<TResult>(Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate, CancellationToken ct = default) {
        return ValueTask.FromResult(List(predicate).Count());
    }

    public ValueTask<long> LongCountAsync<TResult>(Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate, CancellationToken ct = default) {
        return ValueTask.FromResult(List(predicate).LongCount());
    }

    public ValueTask<long> EstimateCountAsync<TResult>(Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate, CancellationToken ct = default) {
        return LongCountAsync(predicate, ct);
    }

    public Task AddAsync(TEntity entity, CancellationToken ct = default) {
        EnsureOpen();
        _pending.Add(entity);
        return Task.CompletedTask;
    }

    public async Task AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken ct = default) {
        foreach (var entity in entities) {
            await AddAsync(entity, ct);
        }
    }

    public Task UpdateAsync(TEntity entity, CancellationToken ct = default) {
        EnsureOpen();
        onUpdate?.Invoke(entity);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(TEntity entity, CancellationToken ct = default) {
        EnsureOpen();
        if (_unit is null) {
            records.Remove(entity);
        } else {
            _removed.Add(entity);
        }

        return Task.CompletedTask;
    }

    public async Task RemoveRangeAsync(IEnumerable<TEntity> entities, CancellationToken ct = default) {
        foreach (var entity in entities) {
            await RemoveAsync(entity, ct);
        }
    }

    public void Dispose() { }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private IEnumerable<TResult> List<TResult>(Func<IQueryable<TEntity>, IQueryable<TResult>>? predicate) {
        return predicate is null ? records.OfType<TResult>() : predicate(records.AsQueryable());
    }

    private void EnsureOpen() {
        if (_committed) {
            throw new InvalidOperationException("Repository was reused after commit.");
        }
    }

    void IReportTestUnitOfWorkParticipant.Commit() => Commit();

    private void Commit() {
        records.AddRange(_pending);
        _pending.Clear();
        foreach (var entity in _removed) {
            records.Remove(entity);
        }

        _removed.Clear();
        _committed = true;
        onCommit?.Invoke();
    }
}

internal interface IReportTestUnitOfWorkParticipant
{
    void Commit();
}

internal sealed class ReportTestUnitOfWork : IUnitOfWork
{
    private readonly List<IReportTestUnitOfWorkParticipant> _participants = [];

    public Task CommitAsync(CancellationToken ct = default) {
        foreach (var participant in _participants) {
            participant.Commit();
        }

        return Task.CompletedTask;
    }

    public Task RollbackAsync(CancellationToken ct = default) => Task.CompletedTask;

    public void Dispose() { }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    internal void Join(IReportTestUnitOfWorkParticipant participant) {
        _participants.Add(participant);
    }
}

internal sealed class ReportTestServiceProvider : IServiceProvider
{
    public object? GetService(Type serviceType) => null;
}

internal sealed class ReportTestDisposable : IDisposable
{
    internal static readonly ReportTestDisposable Instance = new();

    public void Dispose() { }
}
