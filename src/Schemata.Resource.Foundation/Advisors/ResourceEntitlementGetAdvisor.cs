using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Security.Skeleton;

namespace Schemata.Resource.Foundation.Advisors;

public sealed class ResourceEntitlementGetAdvisor<TEntity, TDetail>(IEntitlementProvider<TEntity, GetRequest> entitlement)
    : IResourceGetRequestAdvisor<TEntity, TDetail>
    where TEntity : class, ICanonicalName
    where TDetail : class, ICanonicalName
{
    public int Order => ResourceSecurityAdvisorOrders.Entitlement;
    public async Task<AdviseResult> AdviseAsync(
        AdviceContext                           ctx,
        GetRequest                              request,
        ResourceRequestContainer<TEntity>       container,
        ClaimsPrincipal?                        principal,
        HandledResponse<GetResultBase<TDetail>> handled,
        CancellationToken                       ct = default
    ) {
        var expression = await entitlement.GenerateEntitlementExpressionAsync(
            new() { Operation = nameof(Operations.Get), Request = request }, principal, ct);
        container.ApplyWhere(expression);
        return AdviseResult.Continue;
    }
}