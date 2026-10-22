using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Advice;
using Schemata.Common;
using Schemata.Entity.Repository;
using Schemata.Resource.Foundation.Advisors;

namespace Schemata.Resource.Foundation;

public sealed partial class ResourceOperationHandler<TEntity, TRequest, TDetail, TSummary>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
    where TDetail : class, ICanonicalName
    where TSummary : class, ICanonicalName
{
    /// <summary>
    ///     Creates a resource
    ///     per <seealso href="https://google.aip.dev/133">AIP-133: Standard methods: Create</seealso> through the full advisor
    ///     pipeline.
    /// </summary>
    /// <param name="request">The creation request DTO.</param>
    /// <param name="principal">The optional <see cref="ClaimsPrincipal" />.</param>
    /// <param name="ctx">The advisor context shared with the caller.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <param name="execution">
    ///     The identified dispatch execution carried from the idempotency wrap, or
    ///     <see langword="null" /> when the dispatch runs unidentified. This handler commits every
    ///     dispatch; a non-null execution additionally receives the transaction-completion evidence.
    /// </param>
    /// <returns>A <see cref="CreateResultBase{TDetail}" /> containing the new resource's detail DTO.</returns>
    public Task<CreateResultBase<TDetail>> CreateAsync(
        TRequest                     request,
        ClaimsPrincipal?             principal,
        AdviceContext                ctx,
        CancellationToken?           ct,
        ResourceDispatchExecution?   execution = null
    ) {
        ct ??= CancellationToken.None;
        return CreateCoreAsync(ctx, request, principal, ct.Value, execution);
    }

    /// <summary>
    ///     Runs create processing with an existing advisor context.
    /// </summary>
    /// <param name="ctx">The advisor context shared with the caller.</param>
    /// <param name="request">The creation request DTO.</param>
    /// <param name="principal">The optional <see cref="ClaimsPrincipal" />.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <param name="execution">The identified dispatch execution, or <see langword="null" />.</param>
    /// <returns>A <see cref="CreateResultBase{TDetail}" /> containing the resource detail DTO.</returns>
    internal async Task<CreateResultBase<TDetail>> CreateCoreAsync(
        AdviceContext                 ctx,
        TRequest                      request,
        ClaimsPrincipal?              principal,
        CancellationToken             ct,
        ResourceDispatchExecution?    execution = null
    ) {
        var container = new ResourceRequestContainer<TEntity>();

        var requestOutput = new HandledResponse<CreateResultBase<TDetail>>();
        var requestResult = await HandledResponseRunner.RunAsync(
            requestOutput,
            () => Advisor.For<IResourceCreateRequestAdvisor<TEntity, TRequest, TDetail>>()
                         .RunAsync(ctx, request, container, principal, requestOutput, ct), CollectionNotFound);
        if (requestResult is not null) {
            return requestResult;
        }

        var entity = _mapper.Map<TRequest, TEntity>(request);
        if (entity is null) {
            throw new ValidationException([new() {
                Field       = nameof(request),
                Description = SchemataResources.GetResourceString(SchemataResources.INVALID_PAYLOAD),
                Reason      = SchemataResources.INVALID_PAYLOAD,
            }]);
        }
        AdviceApplyChildParent<TEntity, TRequest, TDetail>.Apply(request, entity);

        var entityOutput = new HandledResponse<CreateResultBase<TDetail>>();
        var entityResult = await HandledResponseRunner.RunAsync(
            entityOutput,
            () => Advisor.For<IResourceCreateAdvisor<TEntity, TRequest, TDetail>>()
                         .RunAsync(ctx, request, entity, principal, entityOutput, ct), CollectionNotFound);
        if (entityResult is not null) {
            return entityResult;
        }

        var mutation = _sp.GetRequiredService<IResourceMutation<TEntity>>();

        return await ExecuteOwnedAsync(
            (transaction, token) => MutateCreateAsync(mutation, entity, transaction, execution, token),
            execution,
            ct);
    }

    private async Task<CreateResultBase<TDetail>> MutateCreateAsync(
        IResourceMutation<TEntity>  mutation,
        TEntity                     entity,
        IUnitOfWork                 transaction,
        ResourceDispatchExecution?  execution,
        CancellationToken           ct
    ) {
        var outcome = await mutation.CreateAsync(entity, transaction, ct);
        if (outcome == MutationResult.NoWrite) {
            // Nothing staged: an identified reservation releases and a retry re-runs deterministically.
            execution?.MarkNotWritten();
            return new();
        }

        var detail = RequireDetail(_mapper.Map<TEntity, TDetail>(entity));
        ResourceCommitOwner.SyncStamp(entity, detail, transaction);

        return new() { Detail = detail };
    }
}
