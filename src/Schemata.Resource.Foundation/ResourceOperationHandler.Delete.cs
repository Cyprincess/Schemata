using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Advice;
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
        CancellationToken? ct,
        bool               allowMissing = false
    ) {
        var (result, _) = await DeleteAsync(name, etag, principal, ct, true, allowMissing);
        return result;
    }

    /// <summary>
    ///     Runs delete processing and returns both the wire result and the affected entity.
    /// </summary>
    /// <param name="name">The resource name.</param>
    /// <param name="etag">The optional ETag for optimistic concurrency.</param>
    /// <param name="principal">The optional <see cref="ClaimsPrincipal" />.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <param name="finalize">Whether to commit the repository and run response advisors.</param>
    /// <param name="allowMissing">Whether a missing resource should produce an empty successful delete result.</param>
    /// <returns>The delete result and the entity that was removed or soft-deleted.</returns>
    internal async Task<(DeleteResultBase<TDetail> Result, TEntity? Entity)> DeleteAsync(
        string             name,
        string?            etag,
        ClaimsPrincipal?   principal,
        CancellationToken? ct,
        bool               finalize,
        bool               allowMissing = false
    ) {
        ct ??= CancellationToken.None;

        var ctx = CreateAdviceContext();

        var gate = await RunPipelineAsync<DeleteResultBase<TDetail>>(
            ctx,
            () => Advisor.For<IResourceRequestAdvisor<TEntity>>()
                         .RunAsync(ctx, principal, nameof(Operations.Delete), ct.Value), () => ResourceNotFound(name),
            () => new());
        if (gate is not null) {
            return (gate, null);
        }

        var req = new DeleteRequest {
            Name = name, Etag = etag, AllowMissing = allowMissing,
        };

        var container = new ResourceRequestContainer<TEntity>();
        ApplyIdentifierPredicates(container, name);

        var requestResult = await RunPipelineAsync<DeleteResultBase<TDetail>>(
            ctx,
            () => Advisor.For<IResourceDeleteRequestAdvisor<TEntity>>()
                         .RunAsync(ctx, req, container, principal, ct.Value), () => ResourceNotFound(name),
            () => new());
        if (requestResult is not null) {
            return (requestResult, null);
        }

        TEntity? entity;
        using (_repository.SuppressQuerySoftDelete()) {
            entity = await _repository.SingleOrDefaultAsync(q => container.Query(q), ct.Value);
        }

        if (entity == null) {
            // AIP-135 allow_missing treats deletion of an absent resource as a successful empty result.
            if (req.AllowMissing) {
                return (new(), null);
            }

            throw ResourceNotFound(name);
        }

        var entityResult = await RunPipelineAsync<DeleteResultBase<TDetail>>(
            ctx, () => Advisor.For<IResourceDeleteAdvisor<TEntity>>().RunAsync(ctx, req, entity, principal, ct.Value),
            () => ResourceNotFound(name), () => new());
        if (entityResult is not null) {
            return (entityResult, entity);
        }

        await _repository.RemoveAsync(entity, ct.Value);

        if (!finalize) {
            return (new(), entity);
        }

        await _repository.CommitAsync(ct.Value);

        // The remove advisors turn the removal into an update for ISoftDelete entities;
        // a populated DeleteTime after commit identifies the soft path, whose response
        // carries the updated resource per AIP-164.
        if (entity is ISoftDelete { DeleteTime: not null }) {
            var detail = _mapper.Map<TEntity, TDetail>(entity);

            var responseResult = await RunPipelineAsync<DeleteResultBase<TDetail>>(
                ctx,
                () => Advisor.For<IResourceResponseAdvisor<TEntity, TDetail>>()
                             .RunAsync(ctx, entity, detail, principal, ct.Value), () => ResourceNotFound(name));
            if (responseResult is not null) {
                return (responseResult, entity);
            }

            return (new() { Detail = detail }, entity);
        }

        return (new(), entity);
    }
}
