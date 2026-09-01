using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Security.Skeleton;

namespace Schemata.Resource.Foundation.Advisors;

public sealed class ResourceListAccessAdvisor<TEntity>(IAccessProvider<TEntity, ListRequest> access)
    : IResourceListRequestAdvisor<TEntity>
    where TEntity : class, ICanonicalName
{
    public int Order => ResourceSecurityAdvisorOrders.Access;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext                     ctx,
        ListRequest                       request,
        ResourceRequestContainer<TEntity> container,
        ClaimsPrincipal?                  principal,
        CancellationToken                 ct = default
    ) {
        if (!AnonymousAccess.IsAnonymous<TEntity>(nameof(Operations.List))) {
            await AuthorizeHelper.EnsureAsync(access,
                                              new() { Operation = nameof(Operations.List), Request = request },
                                              request.Parent ?? string.Empty, principal, ct);
        }

        return AdviseResult.Continue;
    }
}