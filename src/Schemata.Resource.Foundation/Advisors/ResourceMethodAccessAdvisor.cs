using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Security.Skeleton;

namespace Schemata.Resource.Foundation.Advisors;

public sealed class ResourceMethodAccessAdvisor<TEntity, TRequest, TResponse>(IAccessProvider<TEntity, TRequest> access)
    : IResourceMethodAdvisor<TEntity, TRequest, TResponse>
    where TEntity : class, ICanonicalName
    where TRequest : class
    where TResponse : class, ICanonicalName
{
    public int Order => ResourceSecurityAdvisorOrders.Access;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        TRequest          request,
        TEntity           entity,
        ClaimsPrincipal?  principal,
        CancellationToken ct = default
    ) {
        if (!ctx.TryGet<ResourceMethodVerb>(out var method) || method is null
                                                            || AnonymousAccess.IsAnonymous<TEntity>(method.Verb)) {
            return AdviseResult.Continue;
        }

        await AuthorizeHelper.EnsureAsync(access, entity,
                                          new() { Operation = method.Verb, Request = request }, entity.CanonicalName ?? string.Empty, principal, ct);
        return AdviseResult.Continue;
    }
}