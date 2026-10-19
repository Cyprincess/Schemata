using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Entity.Repository.Advisors;

namespace Schemata.Entity.Repository;

/// <summary>
///     Represents a unit of work that coordinates multiple repository operations
///     within a single database transaction. Repositories opt in via
///     <see cref="IRepository.Join" />.
/// </summary>
/// <remarks>
///     The underlying transaction is provider-specific: a buffered provider (EF Core) opens it
///     around the commit-time save, while an immediate-execution provider (LinqToDB) opens it when
///     <see cref="IUnitOfWork{TContext}.Context" /> is first accessed. A unit of work is one-shot:
///     after <see cref="CommitAsync" /> or <see cref="RollbackAsync" /> resolve a new instance from
///     DI to start another transaction.
/// </remarks>
public interface IUnitOfWork : IAsyncDisposable, IDisposable
{
    /// <summary>
    ///     Registers a callback that runs after the unit of work commits its transaction. Sinks run
    ///     in ascending <paramref name="order" />; equal orders keep registration sequence. Use the
    ///     <see cref="CommitOrders" /> segments unless the sink has its own lifecycle position.
    /// </summary>
    /// <param name="order">The ordering segment of the sink.</param>
    /// <param name="sink">The post-commit callback.</param>
    void AddCommitSink(int order, Func<CancellationToken, Task> sink);

    /// <summary>
    ///     Registers a callback that runs when the unit of work rolls back or is disposed before completion.
    /// </summary>
    /// <param name="reset">The rollback callback.</param>
    void AddRollbackSink(Action reset);

    /// <summary>
    ///     Registers a value-projection callback that synchronizes scalar derived values on entities
    ///     already enlisted in this unit of work, at the point where provider-generated write values
    ///     (such as rotated concurrency stamps) are final. A preparation MUST NOT stage or execute
    ///     writes, resolve services, register further callbacks, or produce external effects.
    /// </summary>
    /// <remarks>
    ///     Provider timing follows the execution model: a buffered provider (EF Core) queues the
    ///     preparation and runs it after its commit-time value generation and before the save; an
    ///     immediate-execution provider (LinqToDB) has already executed the staged write, so the
    ///     preparation runs synchronously at registration, before the caller stages any dependent
    ///     write that persists the projected value. A throwing preparation fails the commit on a
    ///     buffered provider (stamps restore and the transaction rolls back) and surfaces at
    ///     registration on an immediate-execution provider (the caller disposes the unit of work to
    ///     roll back).
    /// </remarks>
    /// <param name="preparation">The projection callback.</param>
    void AddSavePreparation(Action preparation);

    /// <summary>
    ///     Commits all pending changes and the database transaction, then runs the enlisted commit
    ///     sinks in ascending order — repository type-level notifications first, then resource and
    ///     domain callbacks. A commit with no writes runs no repository notification.
    /// </summary>
    /// <param name="ct">A cancellation token.</param>
    Task CommitAsync(CancellationToken ct = default);

    /// <summary>
    ///     Rolls back the database transaction and runs the enlisted rollback sinks.
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
    ///     The data context owned by this unit of work. The first access opens the underlying
    ///     connection; subsequent accesses return the same instance until
    ///     <see cref="IUnitOfWork.CommitAsync" /> / <see cref="IUnitOfWork.RollbackAsync" /> /
    ///     disposal. The transaction is opened per the provider's execution model (see the type
    ///     remarks).
    /// </summary>
    TContext Context { get; }
}
