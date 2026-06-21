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
    ///     Gets a resource by name
    ///     per <seealso href="https://google.aip.dev/131">AIP-131: Standard methods: Get</seealso> through the advisor
    ///     pipeline.
    ///     Authorization is checked before the entity is loaded
    ///     per <seealso href="https://google.aip.dev/211">AIP-211: Authorization checks</seealso>.
    /// </summary>
    /// <param name="name">The resource name.</param>
    /// <param name="principal">The optional <see cref="ClaimsPrincipal" />.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A <see cref="GetResultBase{TDetail}" /> containing the detail DTO.</returns>
    public Task<GetResultBase<TDetail>> GetAsync(string name, ClaimsPrincipal? principal, CancellationToken? ct) {
        return GetAsync(new GetRequest { Name = name }, principal, ct);
    }

    /// <summary>
    ///     Gets a resource from a request object that may include a read mask.
    /// </summary>
    /// <param name="request">
    ///     The <see cref="GetRequest" /> carrying the resource name and optional
    ///     <c>read_mask</c> per <seealso href="https://google.aip.dev/157">AIP-157: Partial responses</seealso>.
    /// </param>
    /// <param name="principal">The optional <see cref="ClaimsPrincipal" />.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A <see cref="GetResultBase{TDetail}" /> containing the detail DTO.</returns>
    public async Task<GetResultBase<TDetail>> GetAsync(
        GetRequest         request,
        ClaimsPrincipal?   principal,
        CancellationToken? ct
    ) {
        ct ??= CancellationToken.None;

        var name = request.CanonicalName ?? request.Name ?? string.Empty;

        var ctx = CreateAdviceContext();
        StashReadMask(ctx, request.ReadMask);

        var gate = await RunPipelineAsync<GetResultBase<TDetail>>(
            ctx,
            () => Advisor.For<IResourceRequestAdvisor<TEntity>>()
                         .RunAsync(ctx, principal, nameof(Operations.Get), ct.Value), () => ResourceNotFound(name));
        if (gate is not null) {
            return gate;
        }

        var container = new ResourceRequestContainer<TEntity>();
        ApplyIdentifierPredicates(container, name);

        var requestResult = await RunPipelineAsync<GetResultBase<TDetail>>(
            ctx,
            () => Advisor.For<IResourceGetRequestAdvisor<TEntity>>()
                         .RunAsync(ctx, request, container, principal, ct.Value), () => ResourceNotFound(name));
        if (requestResult is not null) {
            return requestResult;
        }

        TEntity? entity;
        using (_repository.SuppressQuerySoftDelete()) {
            entity = await _repository.SingleOrDefaultAsync(q => container.Query(q), ct.Value);
        }

        if (entity == null) {
            throw ResourceNotFound(name);
        }

        var detail = _mapper.Map<TEntity, TDetail>(entity);

        var responseResult = await RunPipelineAsync<GetResultBase<TDetail>>(
            ctx,
            () => Advisor.For<IResourceResponseAdvisor<TEntity, TDetail>>()
                         .RunAsync(ctx, entity, detail, principal, ct.Value), () => ResourceNotFound(name));
        if (responseResult is not null) {
            return responseResult;
        }

        return new() { Detail = detail };
    }
}
