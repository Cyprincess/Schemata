using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LinqToDB.Data;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.Repository;

namespace Schemata.Entity.LinqToDB;

/// <summary>
///     Unit of work implementation for LinqToDB. The transaction is opened lazily on
///     the first access of <see cref="Context" /> (i.e. the first
///     <see cref="IRepository.Join" /> on an enlisted repository).
/// </summary>
/// <typeparam name="TContext">The <see cref="DataConnection" /> type.</typeparam>
public sealed class LinqToDbUnitOfWork<TContext> : IUnitOfWork<TContext>, IRepositorySource
    where TContext : DataConnection
{
    private readonly Func<TContext>                            _factory;
    private readonly RepositoryBinding                         _binding;
    private readonly IsolationLevel?                           _isolationLevel;
    private          List<(int Order, Func<CancellationToken, Task> Sink)>? _committed;
    private          List<Action>?                             _rollback;
    private          TContext?                                 _context;
    private          DataConnectionTransaction?                _transaction;
    private          Completion                                _completion;
    private          bool                                      _disposed;

    private enum Completion
    {
        Open,
        Committed,
        RolledBack,
        Unknown,
    }

    /// <summary>
    ///     Initializes a unit of work with a data-connection factory and repository binding.
    /// </summary>
    /// <param name="factory">The factory that creates the data connection when the unit of work opens.</param>
    /// <param name="sp">The service provider that resolves optional infrastructure services.</param>
    /// <param name="isolationLevel">The requested isolation, or <see langword="null" /> for the provider default.</param>
    public LinqToDbUnitOfWork(Func<TContext> factory, IServiceProvider sp, IsolationLevel? isolationLevel = null) {
        _factory = factory;
        _binding = sp.GetService<RepositoryBinding>() ?? RepositoryBinding.Default;
        _isolationLevel = isolationLevel;
    }

    public TContext Context
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_completion != Completion.Open) {
                throw new InvalidOperationException("Unit of work already completed. Resolve a new IUnitOfWork instance from a fresh scope to start another transaction.");
            }

            if (_context is not null) {
                return _context;
            }

            // Creation and transaction begin form one ownership step: a BeginTransaction
            // failure disposes the freshly created connection and leaves no internal
            // reference behind, so the next access retries initialization instead of
            // observing a half-initialized non-transactional context.
            var context = _factory();
            DataConnectionTransaction transaction;
            try {
                transaction = _isolationLevel is { } isolation
                    ? context.BeginTransaction(isolation)
                    : context.BeginTransaction();
            }
            catch (Exception ex) when (DatabaseErrorClassifier.IsTransientConflict(ex)) {
                context.Dispose();
                throw new AbortedException(innerException: ex);
            }
            catch {
                context.Dispose();
                throw;
            }

            _context     = context;
            _transaction = transaction;

            return context;
        }
    }

    /// <inheritdoc />
    public string SourceKey { get { return LinqToDbSourceKeys.CreateKey(Context, _factory, _binding); } }

    public async Task CommitAsync(CancellationToken ct = default) {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_completion != Completion.Open) {
            throw new InvalidOperationException("Unit of work already completed.");
        }

        var transaction = _transaction;
        if (transaction is null) {
            _completion = Completion.Committed;
            return;
        }

        try {
            try {
                await transaction.CommitAsync(ct);
            } catch (Exception ex) when (DatabaseErrorClassifier.IsUniqueConstraintViolation(ex)) {
                throw new AlreadyExistsException(innerException: ex);
            } catch (Exception ex) when (DatabaseErrorClassifier.IsTransientConflict(ex)) {
                throw new AbortedException(innerException: ex);
            }
        } catch (Exception ex) {
            _completion = Completion.Unknown;
            List<Exception> failures = [ex];

            // Commit entry makes the outcome unknown even when cleanup rollback succeeds. Cleanup
            // failures follow the commit failure in rollback-then-dispose order.
            try {
                await transaction.RollbackAsync(CancellationToken.None);
            } catch (Exception cleanup) {
                failures.Add(cleanup);
            }

            try {
                await transaction.DisposeAsync();
            } catch (Exception cleanup) {
                failures.Add(cleanup);
            }

            _transaction = null;
            _committed?.Clear();
            _rollback?.Clear();

            if (failures.Count == 1) {
                throw;
            }

            throw new AggregateException(failures);
        }

        _completion = Completion.Committed;

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

        try {
            await transaction.DisposeAsync();
        } catch (Exception ex) {
            (errors ??= []).Add(ex);
        }

        _transaction = null;
        _rollback?.Clear();

        if (errors is not null) {
            throw errors.Count == 1 ? errors.First() : new AggregateException(errors);
        }
    }

    public async Task RollbackAsync(CancellationToken ct = default) {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_completion != Completion.Open) {
            return;
        }

        var transaction = _transaction;
        if (transaction is not null) {
            await transaction.RollbackAsync(ct);
        }

        _completion = Completion.RolledBack;
        List<Exception>? errors = null;
        if (_rollback is not null) {
            try {
                foreach (var reset in _rollback) {
                    reset();
                }
            } catch (Exception ex) {
                errors = [ex];
            }
        }

        if (transaction is not null) {
            try {
                await transaction.DisposeAsync();
            } catch (Exception ex) {
                (errors ??= []).Add(ex);
            }
        }

        _transaction = null;
        _committed?.Clear();
        _rollback?.Clear();

        if (errors is not null) {
            throw errors.Count == 1 ? errors.First() : new AggregateException(errors);
        }
    }

    public void Dispose() {
        if (_disposed) return;

        List<Exception>? errors = null;
        if (_completion == Completion.Open) {
            _completion = Completion.Unknown;
            try {
                _transaction?.Rollback();
                _completion = Completion.RolledBack;
                if (_rollback is not null) foreach (var reset in _rollback) reset();
            } catch (Exception error) {
                (errors ??= []).Add(error);
            }
        }

        try { _transaction?.Dispose(); }
        catch (Exception error) { (errors ??= []).Add(error); }
        _transaction = null;
        _committed?.Clear();
        _rollback?.Clear();
        try { _context?.Dispose(); }
        catch (Exception error) { (errors ??= []).Add(error); }
        _context = null;
        _disposed = true;
        GC.SuppressFinalize(this);
        if (errors is not null) throw errors.Count == 1 ? errors[0] : new AggregateException(errors);
    }

    public async ValueTask DisposeAsync() {
        if (_disposed) return;

        List<Exception>? errors = null;
        if (_completion == Completion.Open) {
            _completion = Completion.Unknown;
            try {
                if (_transaction is not null) await _transaction.RollbackAsync(CancellationToken.None);
                _completion = Completion.RolledBack;
                if (_rollback is not null) foreach (var reset in _rollback) reset();
            } catch (Exception error) {
                (errors ??= []).Add(error);
            }
        }

        try { if (_transaction is not null) await _transaction.DisposeAsync(); }
        catch (Exception error) { (errors ??= []).Add(error); }
        _transaction = null;
        _committed?.Clear();
        _rollback?.Clear();
        try { if (_context is not null) await _context.DisposeAsync(); }
        catch (Exception error) { (errors ??= []).Add(error); }
        _context = null;
        _disposed = true;
        GC.SuppressFinalize(this);
        if (errors is not null) throw errors.Count == 1 ? errors[0] : new AggregateException(errors);
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
        if (_completion != Completion.Open) {
            throw new InvalidOperationException("Unit of work already completed.");
        }

        // Immediate: LinqToDB has already executed the staged write inside the open transaction,
        // so generated values are final and the projection applies synchronously, before the
        // caller stages any dependent write. A throwing preparation surfaces here; the caller's
        // disposal of the uncommitted unit of work rolls the transaction back.
        preparation();
    }

    #endregion

}
