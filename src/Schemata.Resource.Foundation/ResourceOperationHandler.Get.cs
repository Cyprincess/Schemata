using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Advice;
using Schemata.Common;
using Schemata.Security.Skeleton;
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
    ///     Gets a resource from a request object.
    /// </summary>
    /// <param name="request">
    ///     The <see cref="GetRequest" /> carrying the resource name.
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

        var container = new ResourceRequestContainer<TEntity>();
        ResourceIdentifiers.Apply(container, name);

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

        if (entity is null) {
            // The load produced nothing — entitlement filtering and physical absence are
            // indistinguishable here, so the absence is disclosed only when the applicable
            // authorization policy permits it; a denied or undecidable policy is a real
            // PERMISSION_DENIED, never a leaked NOT_FOUND.
            if (!AnonymousAccess.IsAnonymous<TEntity>(nameof(Operations.Get))
             && _sp.GetKeyedService<ResourceAccessStage>(typeof(TEntity)) is { } access) {
                await access.FinalizeMissingAsync<TEntity, GetRequest>(nameof(Operations.Get), request, name, principal, ct.Value);
            }

            throw ResourceNotFound(name);
        }
        var entityResult = await RunPipelineAsync<GetResultBase<TDetail>>(
            ctx, () => Advisor.For<IResourceGetAdvisor<TEntity>>().RunAsync(ctx, request, entity, principal, ct.Value),
            () => ResourceNotFound(name));
        if (entityResult is not null) {
            return entityResult;
        }

        var detail = RequireDetail(_mapper.Map<TEntity, TDetail>(entity));

        return new() { Detail = detail };
    }
}
