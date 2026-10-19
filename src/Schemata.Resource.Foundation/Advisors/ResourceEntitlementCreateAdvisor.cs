using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Security.Skeleton;

namespace Schemata.Resource.Foundation.Advisors;

public sealed class ResourceEntitlementCreateAdvisor<TEntity, TRequest, TDetail>(IEntitlementProvider<TEntity, TRequest> entitlement)
    : IResourceCreateRequestAdvisor<TEntity, TRequest, TDetail>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
    where TDetail : class, ICanonicalName
{
    public int Order => ResourceSecurityAdvisorOrders.Entitlement;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext                              ctx,
        TRequest                                   request,
        ResourceRequestContainer<TEntity>          container,
        ClaimsPrincipal?                           principal,
        HandledResponse<CreateResultBase<TDetail>> handled,
        CancellationToken                          ct = default
    ) {
        var expression = await entitlement.GenerateEntitlementExpressionAsync(
            new() { Operation = nameof(Operations.Create), Request = request }, principal, ct);
        container.ApplyWhere(expression);
        return AdviseResult.Continue;
    }
}