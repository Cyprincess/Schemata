using System;
using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Entity.Repository;

/// <summary>
///     Represents a unit of work that coordinates multiple repository operations
///     within a single database transaction.
/// </summary>
public interface IUnitOfWork : IDisposable
{
    /// <summary>
    ///     Gets a value indicating whether the unit of work has an active transaction.
    /// </summary>
    bool IsActive { get; }

    /// <summary>
    ///     Begins a new database transaction.
    /// </summary>
    void Begin();

    /// <summary>
    ///     Commits all pending changes and the database transaction.
    /// </summary>
    /// <param name="ct">A cancellation token.</param>
    Task CommitAsync(CancellationToken ct = default);

    /// <summary>
    ///     Rolls back the database transaction without committing changes.
    /// </summary>
    /// <param name="ct">A cancellation token.</param>
    Task RollbackAsync(CancellationToken ct = default);
}

/// <summary>
///     Typed unit of work associated with a specific data context type,
///     allowing multiple different repository providers to coexist in the same DI container.
/// </summary>
/// <typeparam name="TContext">The data context type (e.g., <c>DbContext</c> or <c>DataConnection</c>).</typeparam>
public interface IUnitOfWork<TContext> : IUnitOfWork;
