using System;
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
    private readonly TContext               _context;
    private          bool                   _disposed;
    private          IDbContextTransaction? _transaction;

    /// <summary>
    ///     Initializes a new instance of <see cref="EfCoreUnitOfWork{TContext}" />.
    /// </summary>
    /// <param name="context">The database context.</param>
    public EfCoreUnitOfWork(TContext context) { _context = context; }

    #region IUnitOfWork<TContext> Members

    /// <inheritdoc />
    public bool IsActive => _transaction is not null;

    /// <inheritdoc />
    public void Begin() {
        if (_transaction is not null) {
            throw new InvalidOperationException("Unit of work already active.");
        }

        _transaction = _context.Database.BeginTransaction();
    }

    /// <inheritdoc />
    public async Task CommitAsync(CancellationToken ct = default) {
        if (_transaction is null) {
            throw new InvalidOperationException("Unit of work not started.");
        }

        await _context.SaveChangesAsync(ct);
        await _transaction.CommitAsync(ct);
        _transaction.Dispose();
        _transaction = null;
    }

    /// <inheritdoc />
    public async Task RollbackAsync(CancellationToken ct = default) {
        if (_transaction is null) {
            return;
        }

        await _transaction.RollbackAsync(ct);
        _transaction.Dispose();
        _transaction = null;
    }

    /// <inheritdoc />
    public void Dispose() {
        if (_disposed) {
            return;
        }

        if (_transaction is not null) {
            _transaction.Rollback();
            _transaction.Dispose();
        }

        _transaction?.Dispose();
        _disposed = true;

        GC.SuppressFinalize(this);
    }

    #endregion
}
