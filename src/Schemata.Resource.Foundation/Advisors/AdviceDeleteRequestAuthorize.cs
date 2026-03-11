using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Security.Skeleton;

namespace Schemata.Resource.Foundation.Advisors;

public sealed class AdviceDeleteRequestAuthorize<TEntity> : IResourceDeleteRequestAdvisor<TEntity>
    where TEntity : class, IIdentifier
{
    private readonly IAccessProvider<TEntity, ResourceRequestContext<long>> _access;

    public AdviceDeleteRequestAuthorize(IAccessProvider<TEntity, ResourceRequestContext<long>> access) {
        _access = access;
    }

    #region IResourceDeleteRequestAdvisor<TEntity> Members

    public int Order => 100_000_000;

    public int Priority => Order;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        long              id,
        HttpContext?      http,
        CancellationToken ct = default
    ) {
        var result = await _access.HasAccessAsync(null, new() { Operation = Operations.Delete, Request = id }, http?.User, ct);

        if (!result) {
            throw new AuthorizationException();
        }

        return AdviseResult.Continue;
    }

    #endregion
}
