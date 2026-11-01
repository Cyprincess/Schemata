using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Advice;
using Schemata.Common;
using Schemata.Entity.Repository;
using Schemata.Security.Skeleton;
using static Schemata.Security.Skeleton.AnonymousAccess;
using Schemata.Resource.Foundation.Advisors;

namespace Schemata.Resource.Foundation;

public sealed partial class ResourceOperationHandler<TEntity, TRequest, TDetail, TSummary>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
    where TDetail : class, ICanonicalName
    where TSummary : class, ICanonicalName
{
    /// <summary>
    ///     Deletes a resource
    ///     per <seealso href="https://google.aip.dev/135">AIP-135: Standard methods: Delete</seealso> through the full advisor
    ///     pipeline.
    ///     Authorization is checked before the entity is loaded
    ///     per <seealso href="https://google.aip.dev/211">AIP-211: Authorization checks</seealso>.
    ///     A soft delete returns the updated resource
    ///     per <seealso href="https://google.aip.dev/164">AIP-164: Soft delete</seealso>.
    /// </summary>
    /// <param name="name">The resource name.</param>
    /// <param name="etag">
    ///     The optional ETag for optimistic concurrency
    ///     per <seealso href="https://google.aip.dev/154">AIP-154: Resource freshness validation</seealso>.
    /// </param>
    /// <param name="principal">The optional <see cref="ClaimsPrincipal" />.</param>
    /// <param name="ctx">The advisor context shared with the caller.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <param name="allowMissing">Whether a missing resource should produce an empty successful delete result.</param>
    /// <returns>
    ///     A <see cref="DeleteResultBase{TDetail}" /> carrying the soft-deleted resource detail,
    ///     or an empty result for a hard delete.
    /// </returns>
    public async Task<DeleteResultBase<TDetail>> DeleteAsync(
        string             name,
        string?            etag,
        ClaimsPrincipal?   principal,
        AdviceContext      ctx,
        CancellationToken? ct,
        bool               allowMissing = false
    ) {
        ct ??= CancellationToken.None;

        var req = new DeleteRequest {
            Name = name, Etag = etag, AllowMissing = allowMissing,
        };

        var container = new ResourceRequestContainer<TEntity>();
        ResourceIdentifiers.Apply(container, name);

        var requestOutput = new HandledResponse<DeleteResultBase<TDetail>>();
        var requestResult = await HandledResponseRunner.RunAsync(
            requestOutput,
            () => Advisor.For<IResourceDeleteRequestAdvisor<TEntity, TDetail>>()
                         .RunAsync(ctx, req, container, principal, requestOutput, ct.Value), () => ResourceNotFound(name),
            () => new DeleteResultBase<TDetail>());
        if (requestResult is not null) {
            return requestResult;
        }

        var entity = await _repository.SingleOrDefaultAsync(
            q => container.Query(q), ct.Value, new QueryOptions(IgnoreSoftDelete: true));

        if (entity is null) {
            // Entitlement-filtered null and physical absence are indistinguishable; authorize
            // the missing outcome before allow_missing's empty success or a NOT_FOUND — an
            // unauthorized caller learns neither.
            if (!IsAnonymous<TEntity>(nameof(Operations.Delete))
             && _sp.GetKeyedService<ResourceAccessStage>(typeof(TEntity)) is { } access) {
                await access.FinalizeMissingAsync<TEntity, DeleteRequest>(nameof(Operations.Delete), req, name, principal, ct.Value);
            }

            // AIP-135 allow_missing treats deletion of an absent resource as a successful empty result.
            if (req.AllowMissing) {
                return new();
            }

            throw ResourceNotFound(name);
        }

        var entityOutput = new HandledResponse<DeleteResultBase<TDetail>>();
        var entityResult = await HandledResponseRunner.RunAsync(
            entityOutput,
            () => Advisor.For<IResourceDeleteAdvisor<TEntity, TDetail>>().RunAsync(ctx, req, entity, principal, entityOutput, ct.Value),
            () => ResourceNotFound(name), () => new DeleteResultBase<TDetail>());
        if (entityResult is not null) {
            return entityResult;
        }

        // AIP-164: deleting an already-deleted row preserves the original deletion age. The
        // advisors above have run authorization and freshness checks; the loaded tombstone is
        // then absent for allow_missing and not found otherwise, and is never re-deleted.
        if (entity is ISoftDelete { DeleteTime: not null } && req.AllowMissing) {
            return new();
        }

        if (entity is ISoftDelete { DeleteTime: not null }) {
            throw ResourceNotFound(name);
        }

        var mutation = _sp.GetRequiredService<IResourceMutation<TEntity>>();

        return await ExecuteOwnedAsync(
            (transaction, token) => MutateDeleteAsync(mutation, entity, transaction, token),
            null,
            ct.Value);
    }

    private async Task<DeleteResultBase<TDetail>> MutateDeleteAsync(
        IResourceMutation<TEntity> mutation,
        TEntity                    entity,
        IUnitOfWork                transaction,
        CancellationToken          ct
    ) {
        var outcome = await mutation.DeleteAsync(entity, transaction, Operations.Delete, ct);

        // NoWrite means no removal or nested soft-delete update was staged, so no tombstone
        // exists to report.
        if (outcome == MutationResult.NoWrite) {
            return new();
        }

        // The remove advisors turn the removal into an update for ISoftDelete entities;
        // a populated DeleteTime after the mutation identifies the soft path, whose response
        // carries the updated resource per AIP-164. The detail is mapped before this dispatch's
        // commit, so a mapper failure rolls the removal back instead of reporting an error for a
        // persisted delete.
        if (entity is ISoftDelete { DeleteTime: not null }) {
            var detail = _mapper.Map<TEntity, TDetail>(entity);
            if (detail is not null) {
                ResourceCommitOwner.SyncStamp(entity, detail, transaction);
            }

            return new() { Detail = detail };
        }

        return new();
    }
}
