using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Security.Skeleton;

namespace Schemata.Resource.Foundation.Advisors;

public sealed class ResourceListAccessAdvisor<TEntity, TSummary>(IAccessProvider<TEntity, ListRequest> access, IPermissionResolver permissions)
    : IResourceListRequestAdvisor<TEntity, TSummary>
    where TEntity : class, ICanonicalName
    where TSummary : class, ICanonicalName
{
    public int Order => ResourceSecurityAdvisorOrders.Access;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext                                      ctx,
        ListRequest                                        request,
        ResourceRequestContainer<TEntity>                  container,
        ClaimsPrincipal?                                   principal,
        HandledResponse<ListResultBase<TEntity, TSummary>> handled,
        CancellationToken                                  ct = default
    ) {
        if (!AnonymousAccess.IsAnonymous<TEntity>(nameof(Operations.List))) {
            await AuthorizeHelper.EnsureAsync(access, permissions,
                                              new() { Operation = nameof(Operations.List), Request = request, Stage = AccessStage.Target, Parent = request.Parent },
                                              request.Parent ?? string.Empty, principal, ct);
        }

        return AdviseResult.Continue;
    }
}