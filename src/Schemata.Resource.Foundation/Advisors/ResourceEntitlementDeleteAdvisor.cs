using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Security.Skeleton;

namespace Schemata.Resource.Foundation.Advisors;

public sealed class ResourceEntitlementDeleteAdvisor<TEntity>(IEntitlementProvider<TEntity, DeleteRequest> entitlement)
    : IResourceDeleteRequestAdvisor<TEntity>
    where TEntity : class, ICanonicalName
{
    public int Order => ResourceSecurityAdvisorOrders.Entitlement;
    public async Task<AdviseResult> AdviseAsync(
        AdviceContext                     ctx,
        DeleteRequest                     request,
        ResourceRequestContainer<TEntity> container,
        ClaimsPrincipal?                  principal,
        CancellationToken                 ct = default
    ) {
        var expression = await entitlement.GenerateEntitlementExpressionAsync(
            new() { Operation = nameof(Operations.Delete), Request = request }, principal, ct);
        container.ApplyWhere(expression);
        return AdviseResult.Continue;
    }
}