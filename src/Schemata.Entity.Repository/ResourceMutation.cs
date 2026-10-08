using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Entities;
using Schemata.Entity.Repository.Advisors;

namespace Schemata.Entity.Repository;

/// <summary>
///     The default resource mutation owner. Without an outer unit of work each public operation
///     resolves a fresh repository, begins a unit of work, stages, commits, and disposes both; with
///     an outer unit of work it joins and only stages. Domain owners extend staging through the
///     protected <c>Stage*Async</c> seams and never override the public commit orchestration.
/// </summary>
/// <typeparam name="TEntity">The addressed resource entity.</typeparam>
public class ResourceMutation<TEntity> : IResourceMutation<TEntity>
    where TEntity : class
{
    private readonly IServiceProvider _services;

    /// <summary>
    ///     Initializes the mutation owner.
    /// </summary>
    /// <param name="services">The scoped service provider used to resolve repositories and advisors.</param>
    public ResourceMutation(IServiceProvider services) { _services = services; }

    /// <summary>
    ///     The scoped service provider used to resolve repositories and committed advisors.
    /// </summary>
    protected IServiceProvider Services => _services;

    #region IResourceMutation<TEntity> Members

    public Task<MutationResult> CreateAsync(TEntity entity, IUnitOfWork? transaction = null, CancellationToken ct = default) {
        return ExecuteAsync(entity, transaction, Operations.Create, ct);
    }

    public Task<MutationResult> UpdateAsync(
        TEntity           entity,
        IUnitOfWork?      transaction = null,
        Operations        operation   = Operations.Update,
        CancellationToken ct          = default
    ) {
        if (operation is not (Operations.Update or Operations.Undelete)) {
            throw new ArgumentOutOfRangeException(nameof(operation), operation, "Update accepts only Operations.Update or Operations.Undelete.");
        }

        return ExecuteAsync(entity, transaction, operation, ct);
    }

    public Task<MutationResult> DeleteAsync(
        TEntity           entity,
        IUnitOfWork?      transaction = null,
        Operations        operation   = Operations.Delete,
        CancellationToken ct          = default
    ) {
        if (operation is not (Operations.Delete or Operations.Expunge or Operations.Purge)) {
            throw new ArgumentOutOfRangeException(nameof(operation), operation, "Delete accepts only Operations.Delete, Operations.Expunge, or Operations.Purge.");
        }

        return ExecuteAsync(entity, transaction, operation, ct);
    }

    #endregion

    /// <summary>
    ///     Stages a create against the joined repository.
    /// </summary>
    protected virtual Task<MutationResult> StageCreateAsync(
        IRepository<TEntity> repository,
        TEntity              entity,
        IUnitOfWork          transaction,
        CancellationToken    ct
    ) {
        return repository.AddAsync(entity, ct);
    }

    /// <summary>
    ///     Stages an update against the joined repository.
    /// </summary>
    protected virtual Task<MutationResult> StageUpdateAsync(
        IRepository<TEntity> repository,
        TEntity              entity,
        IUnitOfWork          transaction,
        Operations           operation,
        CancellationToken    ct
    ) {
        return repository.UpdateAsync(entity, ct);
    }

    /// <summary>
    ///     Stages a delete against the joined repository. The physical-delete suppression for
    ///     <see cref="Operations.Expunge" /> / <see cref="Operations.Purge" /> is already held by the
    ///     public orchestration around this call.
    /// </summary>
    protected virtual Task<MutationResult> StageDeleteAsync(
        IRepository<TEntity> repository,
        TEntity              entity,
        IUnitOfWork          transaction,
        Operations           operation,
        CancellationToken    ct
    ) {
        return repository.RemoveAsync(entity, ct);
    }

    private async Task<MutationResult> ExecuteAsync(
        TEntity           entity,
        IUnitOfWork?      transaction,
        Operations        operation,
        CancellationToken ct
    ) {
        ArgumentNullException.ThrowIfNull(entity);

        if (transaction is not null) {
            // The joined repository must outlive the outer commit, so the enclosing scope disposes it.
            var joined = _services.GetRequiredService<IRepository<TEntity>>();
            joined.Join(transaction);

            var staged = await StageAsync(joined, entity, transaction, operation, ct);
            if (staged == MutationResult.Applied) {
                EnlistCommitted(entity, operation, transaction);
            }

            return staged;
        }

        await using var repository = _services.GetRequiredService<IRepository<TEntity>>();
        await using var owned = repository.Begin();

        var result = await StageAsync(repository, entity, owned, operation, ct);
        if (result == MutationResult.Applied) {
            EnlistCommitted(entity, operation, owned);
        }

        await owned.CommitAsync(ct);

        return result;
    }

    private async Task<MutationResult> StageAsync(
        IRepository<TEntity> repository,
        TEntity              entity,
        IUnitOfWork          transaction,
        Operations           operation,
        CancellationToken    ct
    ) {
        // Physical-delete intents hold the suppression on the repository that actually stages.
        if (operation is Operations.Expunge or Operations.Purge) {
            using var suppression = repository.SuppressSoftDelete();
            return await StageCoreAsync(repository, entity, transaction, operation, ct);
        }

        return await StageCoreAsync(repository, entity, transaction, operation, ct);
    }

    private Task<MutationResult> StageCoreAsync(
        IRepository<TEntity> repository,
        TEntity              entity,
        IUnitOfWork          transaction,
        Operations           operation,
        CancellationToken    ct
    ) {
        return operation switch {
            Operations.Create => StageCreateAsync(repository, entity, transaction, ct),
            Operations.Update or Operations.Undelete => StageUpdateAsync(repository, entity, transaction, operation, ct),
            _ => StageDeleteAsync(repository, entity, transaction, operation, ct),
        };
    }

    private void EnlistCommitted(TEntity entity, Operations operation, IUnitOfWork transaction) {
        foreach (var advisor in _services.GetServices<IResourceMutationCommittedAdvisor<TEntity>>().OrderBy(advisor => advisor.Order)) {
            var callback = advisor.Prepare(entity, operation);
            if (callback is not null) {
                transaction.AddCommitSink(CommitOrders.Resource, callback);
            }
        }
    }
}
