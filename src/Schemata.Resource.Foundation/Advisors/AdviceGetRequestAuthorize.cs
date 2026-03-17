using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Security.Skeleton;

namespace Schemata.Resource.Foundation.Advisors;

public sealed class AdviceGetRequestAuthorize<TEntity> : IResourceGetRequestAdvisor<TEntity>
    where TEntity : class, ICanonicalName
{
    private readonly IAccessProvider<TEntity, ResourceRequestContext<GetRequest>> _access;

    public AdviceGetRequestAuthorize(IAccessProvider<TEntity, ResourceRequestContext<GetRequest>> access) {
        _access = access;
    }

    #region IResourceGetRequestAdvisor<TEntity> Members

    public int Order => 100_000_000;

    public int Priority => Order;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        GetRequest        request,
        HttpContext?      http,
        CancellationToken ct = default
    ) {
        if (AnonymousAccessHelper.IsAnonymous<TEntity>(Operations.Get)) {
            return AdviseResult.Continue;
        }

        var result = await _access.HasAccessAsync(null, new() { Operation = Operations.Get, Request = request }, http?.User, ct);

        if (!result) {
            throw new AuthorizationException();
        }

        return AdviseResult.Continue;
    }

    #endregion
}
