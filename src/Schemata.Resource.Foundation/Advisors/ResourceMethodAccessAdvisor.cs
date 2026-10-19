using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Security.Skeleton;

namespace Schemata.Resource.Foundation.Advisors;

public sealed class ResourceMethodAccessAdvisor<TEntity, TRequest, TResponse>(IAccessProvider<TEntity, TRequest> access, IPermissionResolver permissions)
    : IResourceMethodAdvisor<TEntity, TRequest, TResponse>
    where TEntity : class, ICanonicalName
    where TRequest : class
    where TResponse : class, ICanonicalName
{
    public int Order => ResourceSecurityAdvisorOrders.Access;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext                                         ctx,
        ResourceMethodExecution<TEntity, TRequest, TResponse> execution,
        CancellationToken                                     ct = default
    ) {
        if (AnonymousAccess.IsAnonymous<TEntity>(execution.Verb)) {
            return AdviseResult.Continue;
        }

        var entity = execution.Entity!;
        await AuthorizeHelper.EnsureAsync(access, permissions, entity,
                                          new() { Operation = execution.Verb, Request = execution.Request, Stage = AccessStage.Instance, Name = entity.CanonicalName }, entity.CanonicalName ?? string.Empty, execution.Principal, ct);
        return AdviseResult.Continue;
    }
}
