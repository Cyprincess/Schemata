using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Schemata.Entity.Repository;

namespace Schemata.Entity.EntityFrameworkCore;

/// <summary>
///     Unit of work implementation for Entity Framework Core, coordinating transactions
///     across repositories that share the same <see cref="DbContext" />.
/// </summary>
/// <typeparam name="TContext">The <see cref="DbContext" /> type.</typeparam>
public sealed class EfCoreUnitOfWork<TContext> : IUnitOfWork<TContext>
    where TContext : DbContext
{
    private readonly List<Func<CancellationToken, Task>> _afterCommit = [];
    private readonly TContext                            _context;
    private          bool                                _disposed;
    private          IDbContextTransaction?              _transaction;

    /// <summary>
    ///     Initializes a new instance of <see cref="EfCoreUnitOfWork{TContext}" />.
    /// </summary>
    /// <param name="context">The database context.</param>
    public EfCoreUnitOfWork(TContext context) { _context = context; }

    #region IUnitOfWork<TContext> Members

    public bool IsActive => _transaction is not null;

    public void Begin() {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_transaction is not null) {
            throw new InvalidOperationException("Unit of work already active.");
        }

        _transaction = _context.Database.BeginTransaction();
    }

    public async Task CommitAsync(CancellationToken ct = default) {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_transaction is null) {
            throw new InvalidOperationException("Unit of work not started.");
        }

        await _context.SaveChangesAsync(ct);
        await _transaction.CommitAsync(ct);
        await _transaction.DisposeAsync();
        _transaction = null;

        await DrainAfterCommitAsync(ct);
    }

    public async Task RollbackAsync(CancellationToken ct = default) {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _afterCommit.Clear();

        if (_transaction is null) {
            return;
        }

        await _transaction.RollbackAsync(ct);
        await _transaction.DisposeAsync();
        _transaction = null;
    }

    public void EnqueueAfterCommit(Func<CancellationToken, Task> action) {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (action is null) {
            throw new ArgumentNullException(nameof(action));
        }

        _afterCommit.Add(action);
    }

    public void Dispose() {
        if (_disposed) {
            return;
        }

        _disposed = true;
        _afterCommit.Clear();

        if (_transaction is not null) {
            try {
                _transaction.Rollback();
            } catch {
                // Suppress rollback failures during Dispose; the transaction may already be
                // completed (committed or rolled back) elsewhere. Dispose must not throw.
            }

            _transaction.Dispose();
            _transaction = null;
        }

        GC.SuppressFinalize(this);
    }

    #endregion

    private async Task DrainAfterCommitAsync(CancellationToken ct) {
        if (_afterCommit.Count == 0) {
            return;
        }

        var pending = _afterCommit.ToArray();
        _afterCommit.Clear();

        List<Exception>? errors = null;
        foreach (var action in pending) {
            try {
                await action(ct);
            } catch (Exception ex) {
                (errors ??= []).Add(ex);
            }
        }

        if (errors is not null) {
            throw errors.Count == 1 ? errors[0] : new AggregateException(errors);
        }
    }
}
