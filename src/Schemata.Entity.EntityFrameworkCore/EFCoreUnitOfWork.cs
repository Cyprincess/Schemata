using System;
using System.Collections.Generic;
using System.Linq;
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
    private readonly IDbContextFactory<TContext>         _factory;
    private readonly List<Func<CancellationToken, Task>> _committed = [];
    private readonly List<Action>                        _rollback  = [];
    private          TContext?                           _context;
    private          IDbContextTransaction?              _transaction;
    private          bool                                _completed;
    private          bool                                _disposed;

    /// <summary>
    ///     Initializes a new instance of <see cref="EfCoreUnitOfWork{TContext}" />.
    /// </summary>
    /// <param name="factory">The database context factory.</param>
    public EfCoreUnitOfWork(IDbContextFactory<TContext> factory) { _factory = factory; }

    #region IUnitOfWork<TContext> Members

    public TContext Context
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_completed) {
                throw new InvalidOperationException("Unit of work already completed. Resolve a new IUnitOfWork instance from a fresh scope to start another transaction.");
            }

            if (_context is not null) {
                return _context;
            }

            _context     = _factory.CreateDbContext();
            _transaction = _context.Database.BeginTransaction();

            return _context;
        }
    }

    public async Task CommitAsync(CancellationToken ct = default) {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_completed) {
            throw new InvalidOperationException("Unit of work already completed.");
        }

        if (_context is null) {
            // No repository ever joined / mutated; degenerate commit is a no-op.
            _completed = true;
            return;
        }

        try {
            await _context.SaveChangesAsync(ct);
            await _transaction!.CommitAsync(ct);
        } catch {
            foreach (var reset in _rollback) reset();
            if (_transaction is not null) {
                try {
                    await _transaction.RollbackAsync(CancellationToken.None);
                } catch {
                    // Transaction may already be completed; rollback during cleanup must not throw.
                }

                await _transaction.DisposeAsync();
                _transaction = null;
            }

            _completed = true;
            _committed.Clear();
            _rollback.Clear();

            throw;
        }

        await _transaction!.DisposeAsync();
        _transaction = null;
        _completed   = true;

        List<Exception>? errors = null;
        foreach (var sink in _committed) {
            try {
                await sink(ct);
            } catch (Exception ex) {
                (errors ??= []).Add(ex);
            }
        }

        _committed.Clear();
        _rollback.Clear();

        if (errors is not null) {
            throw errors.Count == 1 ? errors.First() : new AggregateException(errors);
        }
    }

    public async Task RollbackAsync(CancellationToken ct = default) {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_completed) {
            return;
        }

        foreach (var reset in _rollback) {
            reset();
        }

        if (_transaction is not null) {
            await _transaction.RollbackAsync(ct);
            await _transaction.DisposeAsync();
            _transaction = null;
        }

        _committed.Clear();
        _rollback.Clear();
        _completed = true;
    }

    public void Dispose() {
        if (_disposed) {
            return;
        }

        if (!_completed) {
            foreach (var reset in _rollback) reset();
            if (_transaction is not null) {
                try {
                    _transaction.Rollback();
                } catch {
                    // Transaction may already be completed; rollback during cleanup must not throw.
                }

                _transaction.Dispose();
                _transaction = null;
            }

            _completed = true;
        }

        _committed.Clear();
        _rollback.Clear();

        _context?.Dispose();
        _context = null;

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync() {
        if (_disposed) {
            return;
        }

        if (!_completed) {
            foreach (var reset in _rollback) reset();
            if (_transaction is not null) {
                try {
                    await _transaction.RollbackAsync();
                } catch {
                    // Transaction may already be completed; rollback during cleanup must not throw.
                }

                await _transaction.DisposeAsync();
                _transaction = null;
            }

            _completed = true;
        }

        _committed.Clear();
        _rollback.Clear();

        if (_context is not null) {
            await _context.DisposeAsync();
            _context = null;
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    #endregion

    internal void AddCommitSink(Func<CancellationToken, Task> sink) { _committed.Add(sink); }

    internal void AddRollbackSink(Action reset) { _rollback.Add(reset); }
}
