using System;
using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Entity.Repository;

/// <summary>
///     Represents a unit of work that coordinates multiple repository operations
///     within a single database transaction. Repositories opt in via
///     <see cref="IRepository{TEntity}.Join" />.
/// </summary>
/// <remarks>
///     The transaction is opened lazily on first access to
///     <see cref="IUnitOfWork{TContext}.Context" /> (which the first
///     <see cref="IRepository{TEntity}.Join" /> triggers). A unit of work is one-shot:
///     after <see cref="CommitAsync" /> or <see cref="RollbackAsync" /> resolve a new
///     instance from DI to start another transaction.
/// </remarks>
public interface IUnitOfWork : IAsyncDisposable, IDisposable
{
    /// <summary>
    ///     Commits all pending changes and the database transaction, then notifies
    ///     enlisted repositories to dispatch their
    ///     <see cref="IRepositoryCommittedAdvisor{TEntity}" /> pipelines.
    /// </summary>
    /// <param name="ct">A cancellation token.</param>
    Task CommitAsync(CancellationToken ct = default);

    /// <summary>
    ///     Rolls back the database transaction and resets the tracking lists on every
    ///     enlisted repository.
    /// </summary>
    /// <param name="ct">A cancellation token.</param>
    Task RollbackAsync(CancellationToken ct = default);
}

/// <summary>
///     Typed unit of work associated with a specific data context type.
/// </summary>
/// <typeparam name="TContext">The data context type (e.g., <c>DbContext</c> or <c>DataConnection</c>).</typeparam>
public interface IUnitOfWork<TContext> : IUnitOfWork
{
    /// <summary>
    ///     The data context owned by this unit of work. The first access opens the
    ///     underlying connection and a fresh transaction; subsequent accesses return the
    ///     same instance until <see cref="IUnitOfWork.CommitAsync" /> /
    ///     <see cref="IUnitOfWork.RollbackAsync" /> / disposal.
    /// </summary>
    TContext Context { get; }
}
