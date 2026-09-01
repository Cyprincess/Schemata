using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Security.Skeleton;

namespace Schemata.Resource.Foundation.Advisors;

public sealed class ResourceUpdateAccessAdvisor<TEntity, TRequest>(IAccessProvider<TEntity, TRequest> access)
    : IResourceUpdateAdvisor<TEntity, TRequest>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
{
    public int Order => ResourceSecurityAdvisorOrders.Access;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        TRequest          request,
        TEntity           entity,
        ClaimsPrincipal?  principal,
        CancellationToken ct = default
    ) {
        if (!AnonymousAccess.IsAnonymous<TEntity>(nameof(Operations.Update))) {
            await AuthorizeHelper.EnsureAsync(access, entity,
                                              new() { Operation = nameof(Operations.Update), Request = request },
                                              entity.CanonicalName ?? string.Empty, principal, ct);
        }

        return AdviseResult.Continue;
    }
}