using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Advice;
using Schemata.Resource.Foundation.Advisors;
using static Schemata.Abstractions.SchemataConstants;

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
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A <see cref="CreateResultBase{TDetail}" /> containing the new resource's detail DTO.</returns>
    public Task<CreateResultBase<TDetail>> CreateAsync(
        TRequest           request,
        ClaimsPrincipal?   principal,
        CancellationToken? ct
    ) {
        ct ??= CancellationToken.None;
        var ctx = CreateAdviceContext();
        return CreateCoreAsync(ctx, request, principal, ct.Value, true);
    }

    /// <summary>
    ///     Runs create processing with an existing advisor context.
    /// </summary>
    /// <param name="ctx">The advisor context shared with the caller.</param>
    /// <param name="request">The creation request DTO.</param>
    /// <param name="principal">The optional <see cref="ClaimsPrincipal" />.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <param name="finalize">Whether to commit the repository and run response advisors.</param>
    /// <returns>A <see cref="CreateResultBase{TDetail}" /> containing the resource detail DTO.</returns>
    internal async Task<CreateResultBase<TDetail>> CreateCoreAsync(
        AdviceContext     ctx,
        TRequest          request,
        ClaimsPrincipal?  principal,
        CancellationToken ct,
        bool              finalize
    ) {
        var gate = await RunPipelineAsync<CreateResultBase<TDetail>>(
            ctx,
            () => Advisor.For<IResourceRequestAdvisor<TEntity>>()
                         .RunAsync(ctx, principal, nameof(Operations.Create), ct), CollectionNotFound);
        if (gate is not null) {
            return gate;
        }

        var container = new ResourceRequestContainer<TEntity>();

        var requestResult = await RunPipelineAsync<CreateResultBase<TDetail>>(
            ctx,
            () => Advisor.For<IResourceCreateRequestAdvisor<TEntity, TRequest>>()
                         .RunAsync(ctx, request, container, principal, ct), CollectionNotFound);
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

        var entityResult = await RunPipelineAsync<CreateResultBase<TDetail>>(
            ctx,
            () => Advisor.For<IResourceCreateAdvisor<TEntity, TRequest>>()
                         .RunAsync(ctx, request, entity, principal, ct), CollectionNotFound);
        if (entityResult is not null) {
            return entityResult;
        }

        await _repository.AddAsync(entity, ct);

        if (!finalize) {
            var staged = _mapper.Map<TEntity, TDetail>(entity);
            return new() { Detail = staged };
        }

        await _repository.CommitAsync(ct);

        var detail = _mapper.Map<TEntity, TDetail>(entity);

        var responseResult = await RunPipelineAsync<CreateResultBase<TDetail>>(
            ctx,
            () => Advisor.For<IResourceResponseAdvisor<TEntity, TDetail>>()
                         .RunAsync(ctx, entity, detail, principal, ct), CollectionNotFound);
        if (responseResult is not null) {
            return responseResult;
        }

        return new() { Detail = detail };
    }
}
