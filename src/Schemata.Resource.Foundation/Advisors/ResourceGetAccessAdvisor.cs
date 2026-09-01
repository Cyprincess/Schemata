using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Security.Skeleton;

namespace Schemata.Resource.Foundation.Advisors;

public sealed class ResourceGetAccessAdvisor<TEntity>(IAccessProvider<TEntity, GetRequest> access)
    : IResourceGetAdvisor<TEntity>
    where TEntity : class, ICanonicalName
{
    public int Order => ResourceSecurityAdvisorOrders.Access;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        GetRequest        request,
        TEntity           entity,
        ClaimsPrincipal?  principal,
        CancellationToken ct = default
    ) {
        if (!AnonymousAccess.IsAnonymous<TEntity>(nameof(Operations.Get))) {
            await AuthorizeHelper.EnsureAsync(access, entity,
                                              new() { Operation = nameof(Operations.Get), Request = request },
                                              entity.CanonicalName ?? string.Empty, principal, ct);
        }

        return AdviseResult.Continue;
    }
}