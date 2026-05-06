using System;
using System.Threading;
using System.Threading.Tasks;
using LinqToDB.Data;
using Schemata.Entity.Repository;

namespace Schemata.Entity.LinqToDB;

/// <summary>
///     Unit of work implementation for LINQ to DB, coordinating transactions
///     across repositories that share the same <see cref="DataConnection" />.
/// </summary>
/// <typeparam name="TContext">The <see cref="DataConnection" /> type.</typeparam>
public sealed class LinqToDbUnitOfWork<TContext> : IUnitOfWork<TContext>
    where TContext : DataConnection
{
    private readonly TContext                   _context;
    private          bool                       _disposed;
    private          DataConnectionTransaction? _transaction;

    /// <summary>
    ///     Initializes a new instance of <see cref="LinqToDbUnitOfWork{TContext}" />.
    /// </summary>
    /// <param name="context">The data connection.</param>
    public LinqToDbUnitOfWork(TContext context) { _context = context; }

    #region IUnitOfWork<TContext> Members

    public bool IsActive => _transaction is not null;

    public void Begin() {
        if (_transaction is not null) {
            throw new InvalidOperationException("Unit of work already active.");
        }

        _transaction = _context.BeginTransaction();
    }

    public async Task CommitAsync(CancellationToken ct = default) {
        if (_transaction is null) {
            throw new InvalidOperationException("Unit of work not started.");
        }

        await _transaction.CommitAsync(ct);
        _transaction.Dispose();
        _transaction = null;
    }

    public async Task RollbackAsync(CancellationToken ct = default) {
        if (_transaction is null) {
            return;
        }

        await _transaction.RollbackAsync(ct);
        _transaction.Dispose();
        _transaction = null;
    }

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
