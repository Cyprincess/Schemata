using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Common.Errors;
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
public sealed class EfCoreUnitOfWork<TContext> : IUnitOfWork<TContext>, IRepositorySource
    where TContext : DbContext
{
    private readonly IDbContextFactory<TContext>                         _factory;
    private readonly RepositoryBinding                                   _binding;
    private readonly IsolationLevel?                                     _isolationLevel;
    private          IDbContextTransaction?                              _transaction;
    private          List<Action>?                                          _preparations;
    private          List<(int Order, Func<CancellationToken, Task> Sink)>? _committed;
    private          List<Action>?                                          _rollback;
    private          TContext?                                              _context;
    private          string?                                                _sourceKey;
    private          List<(PropertyEntry Property, object? Value)>?    _rotated;
    private Dictionary<object, (PropertyValues Values, object? Query)>? _admissions;
    private          Completion                                         _completion;
    private          bool                                               _disposed;

    private enum Completion
    {
        Open,
        Committed,
        RolledBack,
        Unknown,
    }

    /// <summary>
    ///     Initializes a new instance of <see cref="EfCoreUnitOfWork{TContext}" />.
    /// </summary>
    /// <param name="factory">The database context factory.</param>
    /// <param name="sp">The service provider the unit of work is constructed in.</param>
    /// <param name="isolationLevel">The requested isolation, or <see langword="null" /> for the existing save pipeline.</param>
    public EfCoreUnitOfWork(IDbContextFactory<TContext> factory, IServiceProvider sp, IsolationLevel? isolationLevel = null) {
        _factory = factory;
        _binding = sp.GetService<RepositoryBinding>() ?? RepositoryBinding.Default;
        _isolationLevel = isolationLevel;
    }

    #region IUnitOfWork<TContext> Members

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

            var context = _factory.CreateDbContext();
            _sourceKey ??= EfCoreSourceKeys.CreateKey(context, _factory, _binding);
            try {
                if (_isolationLevel is { } isolation) {
                    if (!context.Database.IsRelational()) {
                        throw new NotSupportedException("The EF Core provider cannot begin a transaction at the requested isolation level.");
                    }

                    _transaction = context.Database.BeginTransaction(isolation);
                }

                _context = context;
                return context;
            }
            catch (Exception ex) when (DatabaseErrorClassifier.IsTransientConflict(ex)) {
                context.Dispose();
                _transaction = null;
                throw new AbortedException(innerException: ex);
            }
            catch {
                context.Dispose();
                _transaction = null;
                throw;
            }
        }
    }

    /// <inheritdoc />
    public string SourceKey { get { return _sourceKey ??= EfCoreSourceKeys.CreateKey(Context, _factory, _binding); } }

    internal void Admit(EntityEntry entry) {
        var admissions = _admissions ??= new(ReferenceEqualityComparer.Instance);
        if (!admissions.ContainsKey(entry.Entity)) {
            var values = entry.CurrentValues.Clone();
            admissions.Add(entry.Entity, (values, null));
        }
    }

    internal IQueryable<TEntity>? FindAdmission<TEntity>(TEntity entity) where TEntity : class {
        if (_completion != Completion.Open || _disposed || _admissions is null || !_admissions.TryGetValue(entity, out var admission)) return null;
        var entry = Context.Entry(entity);
        if (entry.State != EntityState.Added) return null;
        var key = entry.Metadata.FindPrimaryKey();
        if (key is null) return null;
        foreach (var property in key.Properties) {
            if (property.PropertyInfo is null
             || !Equals(admission.Values[property], property.PropertyInfo.GetValue(entity))
             || !Equals(admission.Values[property], entry.Property(property.Name).CurrentValue)) return null;
        }
        if (admission.Query is null) {
            admission.Query = new[] { (TEntity)admission.Values.ToObject() }.AsQueryable();
            _admissions[entity] = admission;
        }
        return (IQueryable<TEntity>)admission.Query;
    }

    public async Task CommitAsync(CancellationToken ct = default) {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_completion != Completion.Open) {
            throw new InvalidOperationException("Unit of work already completed.");
        }

        if (_context is null) {
            _completion = Completion.Committed;
            return;
        }

        var saveEntered       = false;
        var confirmedRollback = false;
        try {
            try {
                // Rotate concurrency stamps at the true write boundary: repositories stage the
                // caller's stamp as the original value, and the new stamp is generated here, just
                // before the save. Only properties the model configures as Guid concurrency tokens
                // rotate; no interface member is assumed.
                RotateConcurrencyTokens(_context);

                // Value projections run once the generated stamps are final and before the save
                // observes them. Their failures have confirmed no-write evidence because the
                // provider save has not been entered.
                if (_preparations is not null) {
                    foreach (var preparation in _preparations) {
                        preparation();
                    }

                    _preparations.Clear();
                }

                saveEntered = true;
                await _context.SaveChangesAsync(ct);
                if (_transaction is not null) {
                    await _transaction.CommitAsync(ct);
                }
            } catch (Exception ex) when (DatabaseErrorClassifier.IsUniqueConstraintViolation(ex)) {
                confirmedRollback = true;
                throw CreateAlreadyExistsException(ex);
            } catch (Exception ex) when (DatabaseErrorClassifier.IsTransientConflict(ex)) {
                throw new AbortedException(innerException: ex);
            }
        } catch (Exception ex) {
            // Restoring the caller-visible stamp does not assert a database rollback: a lost
            // acknowledgement can follow a durable save. Rollback sinks require explicit evidence.
            RestoreConcurrencyTokens();

            var rolledBack = !saveEntered || confirmedRollback || ex is DbUpdateConcurrencyException;
            _completion = rolledBack ? Completion.RolledBack : Completion.Unknown;

            var failure = CreateConcurrencyException(ex);
            List<Exception> failures = [failure];
            if (_transaction is not null) {
                try {
                    await _transaction.RollbackAsync(CancellationToken.None);
                } catch (Exception cleanup) {
                    failures.Add(cleanup);
                }

                try {
                    await _transaction.DisposeAsync();
                } catch (Exception cleanup) {
                    failures.Add(cleanup);
                }

                _transaction = null;
            }

            if (rolledBack && _rollback is not null) {
                foreach (var reset in _rollback) reset();
            }

            _committed?.Clear();
            _preparations?.Clear();
            _rollback?.Clear();
            _admissions?.Clear();

            if (failures.Count > 1) {
                throw new AggregateException(failures);
            }

            if (!ReferenceEquals(failure, ex)) {
                throw failure;
            }

            throw;
        }

        _completion = Completion.Committed;
        _rotated?.Clear();
        _admissions?.Clear();

        // Post-commit: the database stays committed and the mutation never replays, whatever sinks do.
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

        if (_transaction is not null) {
            try {
                await _transaction.DisposeAsync();
            } catch (Exception ex) {
                (errors ??= []).Add(ex);
            }

            _transaction = null;
        }

        _rollback?.Clear();

        if (errors is not null) {
            throw errors.Count == 1 ? errors.First() : new AggregateException(errors);
        }
    }

    private static Exception CreateConcurrencyException(Exception exception) {
        if (exception is not DbUpdateConcurrencyException concurrency) {
            return exception;
        }

        if (concurrency.Entries.Count == 1) {
            var entity = concurrency.Entries[0].Entity;
            return SchemataResourceErrors.Aborted(entity.GetType(), (entity as ICanonicalName)?.CanonicalName);
        }

        return new AbortedException();
    }

    private void RotateConcurrencyTokens(TContext context) {
        foreach (var entry in context.ChangeTracker.Entries()) {
            if (entry.State != EntityState.Modified) {
                continue;
            }

            foreach (var property in entry.Properties) {
                if (property.Metadata.IsConcurrencyToken && property.Metadata.ClrType == typeof(Guid)) {
                    (_rotated ??= []).Add((property, property.CurrentValue));
                    property.CurrentValue = Guid.NewGuid();
                }
            }
        }
    }

    private void RestoreConcurrencyTokens() {
        if (_rotated is null) {
            return;
        }

        foreach (var (property, value) in _rotated) {
            property.CurrentValue = value;
        }

        _rotated.Clear();
    }

    private static AlreadyExistsException CreateAlreadyExistsException(Exception exception) {
        if (exception is not DbUpdateException ex) {
            return new(innerException: exception);
        }

        // Only an identity the provider actually confirms is exposed: exactly one tracked entry
        // identifies the colliding row; zero or several entries leave the conflict ambiguous
        // (a bulk save may hold many candidates), so no name is guessed.
        var entries = ex.Entries;
        return ClassifyUniqueIdentity(entries.Count == 1 ? [entries[0].Entity] : [], exception);
    }

    /// <summary>
    ///     Applies the unique-violation identity policy to the candidate entities the provider
    ///     reported: a single candidate exposes its canonical resource name, while zero or
    ///     several candidates leave the conflict ambiguous and expose no name.
    /// </summary>
    public static AlreadyExistsException ClassifyUniqueIdentity(IReadOnlyList<object?> candidates, Exception? innerException = null) {
        if (candidates.Count != 1) {
            return new(innerException: innerException);
        }

        var entity = candidates[0];
        return entity is null
                   ? new(innerException: innerException)
                   : SchemataResourceErrors.AlreadyExists(entity.GetType(), (entity as ICanonicalName)?.CanonicalName, innerException: innerException);
    }

    public async Task RollbackAsync(CancellationToken ct = default) {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_completion != Completion.Open) {
            return;
        }

        if (_transaction is not null) {
            await _transaction.RollbackAsync(ct);
        }

        _completion = Completion.RolledBack;
        List<Exception>? errors = null;
        if (_rollback is not null) {
            try {
                foreach (var reset in _rollback) reset();
            } catch (Exception ex) {
                errors = [ex];
            }
        }

        if (_transaction is not null) {
            try {
                await _transaction.DisposeAsync();
            } catch (Exception ex) {
                (errors ??= []).Add(ex);
            }

            _transaction = null;
        }

        _committed?.Clear();
        _rollback?.Clear();
        _admissions?.Clear();

        if (errors is not null) {
            throw errors.Count == 1 ? errors.First() : new AggregateException(errors);
        }
    }

    public void Dispose() {
        if (_disposed) {
            return;
        }

        if (_completion == Completion.Open) {
            if (_transaction is not null) {
                _transaction.Rollback();
                _transaction.Dispose();
                _transaction = null;
            }

            if (_rollback is not null) {
                foreach (var reset in _rollback) reset();
            }

            _completion = Completion.RolledBack;
        }

        _committed?.Clear();
        _rollback?.Clear();
        _admissions?.Clear();

        _context?.Dispose();
        _context = null;

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync() {
        if (_disposed) {
            return;
        }

        if (_completion == Completion.Open) {
            if (_transaction is not null) {
                await _transaction.RollbackAsync();
                await _transaction.DisposeAsync();
                _transaction = null;
            }

            if (_rollback is not null) {
                foreach (var reset in _rollback) reset();
            }

            _completion = Completion.RolledBack;
        }

        _committed?.Clear();
        _rollback?.Clear();
        _admissions?.Clear();

        if (_context is not null) {
            await _context.DisposeAsync();
            _context = null;
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    #endregion

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

        // Queued: EF Core buffers writes, so the projection runs at the commit boundary after the
        // concurrency-stamp rotation and before the save.
        (_preparations ??= []).Add(preparation);
    }

    #endregion
}
