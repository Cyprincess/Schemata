using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.Repository;

namespace Schemata.Entity.EntityFrameworkCore;

/// <summary>
///     Unit of work for Entity Framework Core, coordinating writes across repositories that share
///     the same <see cref="DbContext" />. EF Core buffers changes in the change tracker, so a single
///     <see cref="DbContext.SaveChangesAsync(CancellationToken)" /> at commit persists every enlisted
///     repository's work atomically through the provider's save pipeline. This keeps the unit of
///     work usable against providers such as the in-memory provider and lets concurrent standalone
///     writers serialize at save time after staging completes.
/// </summary>
/// <typeparam name="TContext">The <see cref="DbContext" /> type.</typeparam>
public sealed class EfCoreUnitOfWork<TContext> : IUnitOfWork<TContext>, IUnitOfWorkSink
    where TContext : DbContext
{
    private readonly IDbContextFactory<TContext>         _factory;
    private readonly List<Func<CancellationToken, Task>> _committed = [];
    private readonly List<Action>                        _rollback  = [];
    private          TContext?                           _context;
    private          bool                                _completed;
    private          bool                                _disposed;

    /// <summary>
    ///     Initializes a new instance of <see cref="EfCoreUnitOfWork{TContext}" />.
    /// </summary>
    /// <param name="factory">The database context factory.</param>
    public EfCoreUnitOfWork(IDbContextFactory<TContext> factory) {
        _factory = factory;
    }

    #region IUnitOfWork<TContext> Members

    public TContext Context
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_completed) {
                throw new InvalidOperationException("Unit of work already completed. Resolve a new IUnitOfWork instance from a fresh scope to start another transaction.");
            }

            return _context ??= _factory.CreateDbContext();
        }
    }

    public async Task CommitAsync(CancellationToken ct = default) {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_completed) {
            throw new InvalidOperationException("Unit of work already completed.");
        }

        if (_context is null) {
            _completed = true;
            return;
        }

        try {
            await _context.SaveChangesAsync(ct);
        } catch (Exception ex) {
            foreach (var reset in _rollback) reset();

            _completed = true;
            _committed.Clear();
            _rollback.Clear();

            if (ex is DbUpdateConcurrencyException) {
                throw new AbortedException();
            }

            throw;
        }

        _completed = true;

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

    public Task RollbackAsync(CancellationToken ct = default) {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_completed) {
            return Task.CompletedTask;
        }

        foreach (var reset in _rollback) {
            reset();
        }

        _committed.Clear();
        _rollback.Clear();
        _completed = true;

        return Task.CompletedTask;
    }

    public void Dispose() {
        if (_disposed) {
            return;
        }

        if (!_completed) {
            foreach (var reset in _rollback) reset();
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

    #region IUnitOfWorkSink Members

    void IUnitOfWorkSink.AddCommitSink(Func<CancellationToken, Task> sink) { _committed.Add(sink); }

    void IUnitOfWorkSink.AddRollbackSink(Action reset) { _rollback.Add(reset); }

    #endregion
}
