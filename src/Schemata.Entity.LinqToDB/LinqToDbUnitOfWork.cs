using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LinqToDB.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.Repository;

namespace Schemata.Entity.LinqToDB;

/// <summary>
///     Unit of work implementation for LinqToDB. The transaction is opened lazily on
///     the first access of <see cref="Context" /> (i.e. the first
///     <see cref="IRepository.Join" /> on an enlisted repository).
/// </summary>
/// <typeparam name="TContext">The <see cref="DataConnection" /> type.</typeparam>
public sealed class LinqToDbUnitOfWork<TContext> : IUnitOfWork<TContext>
    where TContext : DataConnection
{
    private readonly Func<TContext>                            _factory;
    private readonly ILogger?                                  _logger;
    private          List<(int Order, Func<CancellationToken, Task> Sink)>? _committed;
    private          List<Action>?                             _rollback;
    private          TContext?                                 _context;
    private          DataConnectionTransaction?                _transaction;
    private          bool                                      _completed;
    private          bool                                      _disposed;

    /// <summary>
    ///     Initializes a unit of work with a data-connection factory and optional logging services.
    /// </summary>
    /// <param name="factory">The factory that creates the data connection when the unit of work opens.</param>
    /// <param name="sp">The service provider that resolves optional infrastructure services.</param>
    public LinqToDbUnitOfWork(Func<TContext> factory, IServiceProvider sp) {
        _factory = factory;
        _logger  = sp.GetService<ILogger<LinqToDbUnitOfWork<TContext>>>();
    }

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

            _context     = _factory();
            _transaction = _context.BeginTransaction();

            return _context;
        }
    }

    public async Task CommitAsync(CancellationToken ct = default) {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_completed) {
            throw new InvalidOperationException("Unit of work already completed.");
        }

        if (_transaction is null) {
            _completed = true;
            return;
        }

        try {
            try {
                await _transaction.CommitAsync(ct);
            } catch (Exception ex) when (DatabaseErrorClassifier.IsUniqueConstraintViolation(ex)) {
                throw new AlreadyExistsException(innerException: ex);
            }
        } catch (Exception) {
            if (_rollback is not null) {
                foreach (var reset in _rollback) {
                    reset();
                }
            }

            await TryRollbackAsync(_transaction);
            await _transaction.DisposeAsync();

            _transaction = null;
            _completed   = true;
            _committed?.Clear();
            _rollback?.Clear();

            throw;
        }

        await _transaction.DisposeAsync();

        _transaction = null;
        _completed   = true;

        List<Exception>? errors = null;
        if (_committed is not null) {
            foreach (var (_, sink) in _committed) {
                try {
                    await sink(ct);
                } catch (Exception ex) {
                    (errors ??= []).Add(ex);
                }
            }

            _committed.Clear();
        }

        _rollback?.Clear();

        if (errors is not null) {
            throw errors.Count == 1 ? errors.First() : new AggregateException(errors);
        }
    }

    public async Task RollbackAsync(CancellationToken ct = default) {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_completed) {
            return;
        }

        if (_rollback is not null) {
            foreach (var reset in _rollback) {
                reset();
            }
        }

        if (_transaction is not null) {
            await _transaction.RollbackAsync(ct);
            await _transaction.DisposeAsync();
            _transaction = null;
        }

        _committed?.Clear();
        _rollback?.Clear();
        _completed = true;
    }

    public void Dispose() {
        if (_disposed) return;

        if (!_completed) {
            if (_rollback is not null) {
                foreach (var reset in _rollback) reset();
            }

            if (_transaction is not null) {
                TryRollback(_transaction);
                _transaction.Dispose();
                _transaction = null;
            }

            _completed = true;
        }

        _committed?.Clear();
        _rollback?.Clear();

        _context?.Dispose();
        _context = null;

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync() {
        if (_disposed) return;

        if (!_completed) {
            if (_rollback is not null) {
                foreach (var reset in _rollback) reset();
            }

            if (_transaction is not null) {
                await TryRollbackAsync(_transaction);
                await _transaction.DisposeAsync();
                _transaction = null;
            }

            _completed = true;
        }

        _committed?.Clear();
        _rollback?.Clear();

        if (_context is not null) {
            await _context.DisposeAsync();
            _context = null;
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    #region IUnitOfWork Members

    void IUnitOfWork.AddCommitSink(int order, Func<CancellationToken, Task> sink) {
        var committed = _committed ??= [];

        // Stable ordered insert: ascending order, equal orders keep registration sequence.
        var index = committed.Count;
        while (index > 0 && committed[index - 1].Order > order) {
            index--;
        }

        committed.Insert(index, (order, sink));
    }

    void IUnitOfWork.AddRollbackSink(Action reset) { (_rollback ??= []).Add(reset); }

    void IUnitOfWork.AddSavePreparation(Action preparation) {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_completed) {
            throw new InvalidOperationException("Unit of work already completed.");
        }

        // Immediate: LinqToDB has already executed the staged write inside the open transaction,
        // so generated values are final and the projection applies synchronously, before the
        // caller stages any dependent write. A throwing preparation surfaces here; the caller's
        // disposal of the uncommitted unit of work rolls the transaction back.
        preparation();
    }

    #endregion

    // Rollback during commit-failure or disposal cleanup must not throw: the transaction may already
    // be completed, and an exception here would mask the original failure.
    private async Task TryRollbackAsync(DataConnectionTransaction transaction) {
        try {
            await transaction.RollbackAsync(CancellationToken.None);
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "Rollback during unit-of-work cleanup failed.");
        }
    }

    private void TryRollback(DataConnectionTransaction transaction) {
        try {
            transaction.Rollback();
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "Rollback during unit-of-work cleanup failed.");
        }
    }
}
