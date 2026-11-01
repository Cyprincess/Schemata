using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Security.Skeleton;

namespace Schemata.Resource.Foundation.Advisors;

public sealed class ResourceDeleteAccessAdvisor<TEntity, TDetail>(IAccessProvider<TEntity, DeleteRequest> access, IPermissionResolver permissions)
    : IResourceDeleteAdvisor<TEntity, TDetail>
    where TEntity : class, ICanonicalName
    where TDetail : class, ICanonicalName
{
    public int Order => ResourceSecurityAdvisorOrders.Access;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext                              ctx,
        DeleteRequest                              request,
        TEntity                                    entity,
        ClaimsPrincipal?                           principal,
        HandledResponse<DeleteResultBase<TDetail>> handled,
        CancellationToken                          ct = default
    ) {
        if (!AnonymousAccess.IsAnonymous<TEntity>(nameof(Operations.Delete))) {
            await AuthorizeHelper.EnsureAsync(access, permissions, entity,
                                              new() { Operation = nameof(Operations.Delete), Request = request, Stage = AccessStage.Instance, Name = entity.CanonicalName },
                                              entity.CanonicalName ?? string.Empty, principal, ct);
        }

        return AdviseResult.Continue;
    }
}