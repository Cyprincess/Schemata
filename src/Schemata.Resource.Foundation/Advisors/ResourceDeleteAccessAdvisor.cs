using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Security.Skeleton;

namespace Schemata.Resource.Foundation.Advisors;

public sealed class ResourceDeleteAccessAdvisor<TEntity>(IAccessProvider<TEntity, DeleteRequest> access)
    : IResourceDeleteAdvisor<TEntity>
    where TEntity : class, ICanonicalName
{
    public int Order => ResourceSecurityAdvisorOrders.Access;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        DeleteRequest     request,
        TEntity           entity,
        ClaimsPrincipal?  principal,
        CancellationToken ct = default
    ) {
        if (!AnonymousAccess.IsAnonymous<TEntity>(nameof(Operations.Delete))) {
            await AuthorizeHelper.EnsureAsync(access, entity,
                                              new() { Operation = nameof(Operations.Delete), Request = request },
                                              entity.CanonicalName ?? string.Empty, principal, ct);
        }

        return AdviseResult.Continue;
    }
}