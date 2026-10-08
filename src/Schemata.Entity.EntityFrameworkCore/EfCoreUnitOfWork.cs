using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
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
public sealed class EfCoreUnitOfWork<TContext> : IUnitOfWork<TContext>
    where TContext : DbContext
{
    private readonly IDbContextFactory<TContext>                         _factory;
    private          List<Action>?                                          _preparations;
    private          List<(int Order, Func<CancellationToken, Task> Sink)>? _committed;
    private          List<Action>?                                          _rollback;
    private          TContext?                                              _context;
    private          bool                                                _completed;
    private          bool                                                _disposed;

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
            try {
                // Rotate concurrency stamps at the true write boundary: repositories stage the
                // caller's stamp as the original value, and the new stamp is generated here, just
                // before the save. Only properties the model configures as Guid concurrency tokens
                // rotate; no interface member is assumed.
                RotateConcurrencyTokens(_context);

                // Value projections run once the generated stamps are final and before the save
                // observes them; a throwing preparation fails the commit through the restore and
                // rollback path below, exactly like a failed save.
                if (_preparations is not null) {
                    foreach (var preparation in _preparations) {
                        preparation();
                    }

                    _preparations.Clear();
                }

                await _context.SaveChangesAsync(ct);
            } catch (Exception ex) when (DatabaseErrorClassifier.IsUniqueConstraintViolation(ex)) {
                throw CreateAlreadyExistsException(ex);
            }
        } catch (Exception ex) {
            // The save failed: restore the caller-visible stamps through the live tracker (failed
            // saves leave entries Modified, so the same traversal reaches every rotated property)
            // before running rollback sinks, so a retried operation starts from pre-commit values.
            RestoreConcurrencyTokens(_context);

            if (_rollback is not null) {
                foreach (var reset in _rollback) reset();
            }

            _completed = true;
            _committed?.Clear();
            _preparations?.Clear();
            _rollback?.Clear();

            if (ex is DbUpdateConcurrencyException concurrency) {
                if (concurrency.Entries.Count == 1) {
                    var entity = concurrency.Entries[0].Entity;
                    throw SchemataResourceErrors.Aborted(entity.GetType(), (entity as ICanonicalName)?.CanonicalName);
                }
                throw new AbortedException();
            }

            throw;
        }

        _completed = true;

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

        _rollback?.Clear();

        if (errors is not null) {
            throw errors.Count == 1 ? errors.First() : new AggregateException(errors);
        }
    }

    private static void RotateConcurrencyTokens(TContext context) {
        foreach (var entry in context.ChangeTracker.Entries()) {
            if (entry.State != EntityState.Modified) {
                continue;
            }

            foreach (var property in entry.Properties) {
                if (property.Metadata.IsConcurrencyToken && property.Metadata.ClrType == typeof(Guid)) {
                    property.CurrentValue = Guid.NewGuid();
                }
            }
        }
    }

    private static void RestoreConcurrencyTokens(TContext context) {
        foreach (var entry in context.ChangeTracker.Entries()) {
            if (entry.State != EntityState.Modified) {
                continue;
            }

            foreach (var property in entry.Properties) {
                if (property.Metadata.IsConcurrencyToken && property.Metadata.ClrType == typeof(Guid)) {
                    property.CurrentValue = property.OriginalValue;
                }
            }
        }
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

    public Task RollbackAsync(CancellationToken ct = default) {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_completed) {
            return Task.CompletedTask;
        }

        if (_rollback is not null) {
            foreach (var reset in _rollback) reset();
        }

        _committed?.Clear();
        _rollback?.Clear();
        _completed = true;

        return Task.CompletedTask;
    }

    public void Dispose() {
        if (_disposed) {
            return;
        }

        if (!_completed) {
            if (_rollback is not null) {
                foreach (var reset in _rollback) reset();
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
        if (_disposed) {
            return;
        }

        if (!_completed) {
            if (_rollback is not null) {
                foreach (var reset in _rollback) reset();
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
        if (_completed) {
            throw new InvalidOperationException("Unit of work already completed.");
        }

        // Queued: EF Core buffers writes, so the projection runs at the commit boundary after the
        // concurrency-stamp rotation and before the save.
        (_preparations ??= []).Add(preparation);
    }

    #endregion
}
