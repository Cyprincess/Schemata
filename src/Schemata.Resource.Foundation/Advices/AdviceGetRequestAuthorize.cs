using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Advices;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Security.Skeleton;

namespace Schemata.Resource.Foundation.Advices;

public sealed class AdviceGetRequestAuthorize<TEntity> : IResourceGetRequestAdvice<TEntity>
    where TEntity : class, IIdentifier
{
    private readonly IAccessProvider<TEntity, ResourceRequestContext<long>> _access;

    public AdviceGetRequestAuthorize(IAccessProvider<TEntity, ResourceRequestContext<long>> access) {
        _access = access;
    }

    #region IResourceGetRequestAdvice<TEntity> Members

    public int Order => 100_000_000;

    public int Priority => Order;

    public async Task<bool> AdviseAsync(
        AdviceContext     ctx,
        long              id,
        HttpContext       http,
        CancellationToken ct = default) {
        var result = await _access.HasAccessAsync(null, new() {
            Operation = Operations.Get,
            Request   = id,
        }, http.User, ct);

        if (!result) {
            throw new AuthorizationException();
        }

        return true;
    }

    #endregion
}
