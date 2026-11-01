using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;

namespace Schemata.Resource.Foundation;

/// <summary>
///     The single commit owner for one resource mutation: begins the unit of work through the
///     owning repository, runs every fail-able step of the dispatch inside the caller's
///     <c>mutate</c> delegate, and commits exactly once after that delegate produced its response.
/// </summary>
/// <remarks>
///     <para>
///         Standard-method handlers (create, update, delete, undelete, and expunge) use this owner,
///         so a mapping or response-shaping failure observed inside <c>mutate</c> rolls the
///         transaction back instead of leaving a committed write whose response failed.
///     </para>
///     <para>
///         An identified dispatch (AIP-155) records its completion through the unit of work's commit
///         and rollback sinks, so the idempotency wrap observes the outcome of this same
///         transaction rather than inferring one from the returned response or an exception.
///     </para>
/// </remarks>
internal static class ResourceCommitOwner
{
    /// <summary>
    ///     Runs <paramref name="mutate" /> as the only body of one transaction and commits it once.
    /// </summary>
    /// <typeparam name="TEntity">The persistent entity type the owning repository manages.</typeparam>
    /// <typeparam name="TResult">The response the mutate delegate shapes from the committed write.</typeparam>
    /// <param name="repository">The repository that begins the dispatch's unit of work.</param>
    /// <param name="execution">
    ///     The identified dispatch execution, or <see langword="null" /> when the dispatch runs
    ///     unidentified; only the completion evidence is recorded here.
    /// </param>
    /// <param name="mutate">
    ///     The mutation and every fail-able response preparation that must happen before the commit.
    /// </param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The value produced by <paramref name="mutate" /> after the commit completes.</returns>
    public static async Task<TResult> ExecuteAsync<TEntity, TResult>(
        IRepository<TEntity>                                 repository,
        ResourceDispatchExecution?                            execution,
        Func<IUnitOfWork, CancellationToken, Task<TResult>>   mutate,
        CancellationToken                                    ct = default
    ) where TEntity : class {
        await using var transaction = repository.Begin();

        if (execution is not null) {
            // The stamp sink registers ahead of the mutation's join and its repository notifications,
            // so a throwing post-commit sink can never leave a persisted dispatch recorded as unwritten.
            execution.MarkReached();
            transaction.AddCommitSink(CommitOrders.Repository - 1, _ => {
                execution.MarkCommitted();
                return Task.CompletedTask;
            });
            transaction.AddRollbackSink(execution.MarkRolledBack);
        }

        var response = await mutate(transaction, ct);

        // CommitAsync resolving normally is the owner's completion evidence: the provider
        // transaction ended committed before the sinks ran.
        await transaction.CommitAsync(ct);
        execution?.MarkCommitted();

        return response;
    }

    /// <summary>
    ///     Copies the final concurrency stamp from the staged entity onto the response detail once
    ///     provider-generated write values are final: immediately on immediate-execution providers,
    ///     after stamp rotation on buffered ones. Keeps the pre-commit mapped detail's ETag equal to
    ///     the persisted row's stamp.
    /// </summary>
    /// <typeparam name="TEntity">The persistent entity type.</typeparam>
    /// <typeparam name="TDetail">The detail DTO type.</typeparam>
    /// <param name="entity">The staged entity carrying the final provider values.</param>
    /// <param name="detail">The detail mapped before the commit.</param>
    /// <param name="transaction">The dispatch's unit of work that will commit the write.</param>
    public static void SyncStamp<TEntity, TDetail>(TEntity entity, TDetail detail, IUnitOfWork transaction)
        where TEntity : class
        where TDetail : class {
        transaction.AddSavePreparation(() => {
            if (entity is IConcurrency source && detail is IConcurrency stamped) {
                stamped.Timestamp = source.Timestamp;
            }
        });
    }
}
