using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Security.Skeleton;

namespace Schemata.Resource.Foundation.Advisors;

public sealed class ResourceCreateAccessAdvisor<TEntity, TRequest>(IAccessProvider<TEntity, TRequest> access)
    : IResourceCreateAdvisor<TEntity, TRequest>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
{
    public int Order => ResourceSecurityAdvisorOrders.Access;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext ctx,
        TRequest request,
        TEntity entity,
        ClaimsPrincipal? principal,
        CancellationToken ct = default
    ) {
        if (!AnonymousAccess.IsAnonymous<TEntity>(nameof(Operations.Create))) {
            await AuthorizeHelper.EnsureAsync(access,
                new() { Operation = nameof(Operations.Create), Request = request },
                request.Name ?? string.Empty, principal, ct);
        }

        return AdviseResult.Continue;
    }
}