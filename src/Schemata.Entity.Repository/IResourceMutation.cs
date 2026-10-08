using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Entities;

namespace Schemata.Entity.Repository;

/// <summary>
///     The single owner boundary for all AIP resource-semantic writes. Every resource write — from the
///     standard resource handlers or from a domain owner — flows through this contract; the default
///     <see cref="ResourceMutation{TEntity}" /> implementation owns transaction orchestration, and
///     closed domain owners extend staging only.
/// </summary>
/// <remarks>
///     When no unit of work is supplied the mutation owns the transaction: it resolves a fresh
///     transient repository, begins a unit of work, stages, commits, and disposes. When a unit of
///     work is passed the mutation joins it and only stages/enlists; commit, rollback, and disposal
///     belong to the outer owner.
/// </remarks>
/// <typeparam name="TEntity">The addressed resource entity.</typeparam>
public interface IResourceMutation<TEntity> where TEntity : class
{
    /// <summary>
    ///     Creates the resource. Non-null entities only; null handling stays at store/manager adapter
    ///     boundaries.
    /// </summary>
    /// <param name="entity">The resource to create.</param>
    /// <param name="transaction">An optional outer unit of work to enlist in.</param>
    /// <param name="ct">A cancellation token.</param>
    Task<MutationResult> CreateAsync(TEntity entity, IUnitOfWork? transaction = null, CancellationToken ct = default);

    /// <summary>
    ///     Updates the resource. <paramref name="operation" /> carries the resource action intent and
    ///     accepts only <see cref="Operations.Update" /> or <see cref="Operations.Undelete" />.
    /// </summary>
    /// <param name="entity">The resource to update.</param>
    /// <param name="transaction">An optional outer unit of work to enlist in.</param>
    /// <param name="operation">The resource action intent.</param>
    /// <param name="ct">A cancellation token.</param>
    Task<MutationResult> UpdateAsync(
        TEntity           entity,
        IUnitOfWork?      transaction = null,
        Operations        operation   = Operations.Update,
        CancellationToken ct          = default
    );

    /// <summary>
    ///     Deletes the resource. <paramref name="operation" /> carries the resource action intent and
    ///     accepts only <see cref="Operations.Delete" />, <see cref="Operations.Expunge" />, or
    ///     <see cref="Operations.Purge" />; physical-delete intents scope
    ///     <see cref="IRepository.SuppressSoftDelete" /> on the repository actually used by the
    ///     mutation.
    /// </summary>
    /// <param name="entity">The resource to delete.</param>
    /// <param name="transaction">An optional outer unit of work to enlist in.</param>
    /// <param name="operation">The resource action intent.</param>
    /// <param name="ct">A cancellation token.</param>
    Task<MutationResult> DeleteAsync(
        TEntity           entity,
        IUnitOfWork?      transaction = null,
        Operations        operation   = Operations.Delete,
        CancellationToken ct          = default
    );
}
