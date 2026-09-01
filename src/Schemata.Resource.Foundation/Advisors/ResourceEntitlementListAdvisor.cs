using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Security.Skeleton;

namespace Schemata.Resource.Foundation.Advisors;

public sealed class ResourceEntitlementListAdvisor<TEntity>(IEntitlementProvider<TEntity, ListRequest> entitlement)
    : IResourceListRequestAdvisor<TEntity>
    where TEntity : class, ICanonicalName
{
    public int Order => ResourceSecurityAdvisorOrders.Entitlement;
    public async Task<AdviseResult> AdviseAsync(
        AdviceContext                     ctx,
        ListRequest                       request,
        ResourceRequestContainer<TEntity> container,
        ClaimsPrincipal?                  principal,
        CancellationToken                 ct = default
    ) {
        var expression = await entitlement.GenerateEntitlementExpressionAsync(
            new() { Operation = nameof(Operations.List), Request = request }, principal, ct);
        container.ApplyWhere(expression);
        return AdviseResult.Continue;
    }
}