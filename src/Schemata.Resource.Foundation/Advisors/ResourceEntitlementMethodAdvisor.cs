using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Common;
using Schemata.Security.Skeleton;

namespace Schemata.Resource.Foundation.Advisors;

public sealed class ResourceEntitlementMethodAdvisor<TEntity, TRequest>(IEntitlementProvider<TEntity, TRequest> entitlement)
    : IResourceMethodRequestAdvisor<TEntity, TRequest>
    where TEntity : class, ICanonicalName
    where TRequest : class
{
    public int Order => ResourceSecurityAdvisorOrders.Entitlement;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext                     ctx,
        TRequest                          request,
        ResourceRequestContainer<TEntity> container,
        ClaimsPrincipal?                  principal,
        CancellationToken                 ct = default
    ) {
        if (!ctx.TryGet<ResourceMethodVerb>(out var method) || method is null) {
            return AdviseResult.Continue;
        }

        var expression = await entitlement.GenerateEntitlementExpressionAsync(
            new() { Operation = method.Verb, Request = request }, principal, ct);
        container.ApplyWhere(expression);
        return AdviseResult.Continue;
    }
}