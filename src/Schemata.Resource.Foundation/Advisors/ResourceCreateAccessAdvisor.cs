using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Security.Skeleton;

namespace Schemata.Resource.Foundation.Advisors;

public sealed class ResourceCreateAccessAdvisor<TEntity, TRequest, TDetail>(IAccessProvider<TEntity, TRequest> access)
    : IResourceCreateAdvisor<TEntity, TRequest, TDetail>
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
        HandledResponse<CreateResultBase<TDetail>> handled,
        CancellationToken                          ct = default
    ) {
        if (!AnonymousAccess.IsAnonymous<TEntity>(nameof(Operations.Create))) {
            await AuthorizeHelper.EnsureAsync(access,
                entity,
                new() { Operation = nameof(Operations.Create), Request = request, Stage = AccessStage.Instance, Name = entity.CanonicalName },
                entity.CanonicalName ?? string.Empty, principal, ct);
        }

        return AdviseResult.Continue;
    }
}