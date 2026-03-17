using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Security.Skeleton;

namespace Schemata.Resource.Foundation.Advisors;

public sealed class AdviceDeleteRequestAuthorize<TEntity> : IResourceDeleteRequestAdvisor<TEntity>
    where TEntity : class, ICanonicalName
{
    private readonly IAccessProvider<TEntity, ResourceRequestContext<DeleteRequest>> _access;

    public AdviceDeleteRequestAuthorize(IAccessProvider<TEntity, ResourceRequestContext<DeleteRequest>> access) {
        _access = access;
    }

    #region IResourceDeleteRequestAdvisor<TEntity> Members

    public int Order => 100_000_000;

    public int Priority => Order;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        DeleteRequest     request,
        HttpContext?      http,
        CancellationToken ct = default
    ) {
        if (AnonymousAccessHelper.IsAnonymous<TEntity>(Operations.Delete)) {
            return AdviseResult.Continue;
        }

        var result = await _access.HasAccessAsync(null, new() { Operation = Operations.Delete, Request = request }, http?.User, ct);

        if (!result) {
            throw new AuthorizationException();
        }

        return AdviseResult.Continue;
    }

    #endregion
}
