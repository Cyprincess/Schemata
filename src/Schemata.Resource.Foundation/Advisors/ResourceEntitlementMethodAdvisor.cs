using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Common;
using Schemata.Security.Skeleton;

namespace Schemata.Resource.Foundation.Advisors;

public sealed class ResourceEntitlementMethodAdvisor<TEntity, TRequest, TResponse>(IEntitlementProvider<TEntity, TRequest> entitlement)
    : IResourceMethodRequestAdvisor<TEntity, TRequest, TResponse>
    where TEntity : class, ICanonicalName
    where TRequest : class
    where TResponse : class
{
    public int Order => ResourceSecurityAdvisorOrders.Entitlement;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext                                         ctx,
        ResourceMethodExecution<TEntity, TRequest, TResponse> execution,
        ResourceRequestContainer<TEntity>                     container,
        CancellationToken                                     ct = default
    ) {
        var expression = await entitlement.GenerateEntitlementExpressionAsync(
            new() { Operation = execution.Verb, Request = execution.Request }, execution.Principal, ct);
        container.ApplyWhere(expression);
        return AdviseResult.Continue;
    }
}
