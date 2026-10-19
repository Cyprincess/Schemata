using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Security.Skeleton;

namespace Schemata.Resource.Foundation.Advisors;

public sealed class ResourceUpdateAccessAdvisor<TEntity, TRequest, TDetail>(IAccessProvider<TEntity, TRequest> access)
    : IResourceUpdateAdvisor<TEntity, TRequest, TDetail>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
    where TDetail : class, ICanonicalName
{
    public int Order => ResourceSecurityAdvisorOrders.Access;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext                              ctx,
        TRequest                                   request,
        TEntity                                    entity,
        ClaimsPrincipal?                           principal,
        HandledResponse<UpdateResultBase<TDetail>> handled,
        CancellationToken                          ct = default
    ) {
        if (!AnonymousAccess.IsAnonymous<TEntity>(nameof(Operations.Update))) {
            await AuthorizeHelper.EnsureAsync(access, entity,
                                              new() { Operation = nameof(Operations.Update), Request = request, Stage = AccessStage.Instance, Name = entity.CanonicalName },
                                              entity.CanonicalName ?? string.Empty, principal, ct);
        }

        return AdviseResult.Continue;
    }
}