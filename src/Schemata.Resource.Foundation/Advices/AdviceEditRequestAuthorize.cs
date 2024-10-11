using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Advices;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Security.Skeleton;

namespace Schemata.Resource.Foundation.Advices;

public sealed class AdviceEditRequestAuthorize<TEntity, TRequest> : IResourceEditRequestAdvice<TEntity, TRequest>
    where TEntity : class, IIdentifier
    where TRequest : class, IIdentifier
{
    private readonly IAccessProvider<TEntity, ResourceRequestContext<TRequest>> _access;

    public AdviceEditRequestAuthorize(IAccessProvider<TEntity, ResourceRequestContext<TRequest>> access) {
        _access = access;
    }

    #region IResourceEditRequestAdvice<TEntity,TRequest> Members

    public int Order => 100_000_000;

    public int Priority => Order;

    public async Task<bool> AdviseAsync(
        AdviceContext     ctx,
        long              id,
        TRequest          request,
        HttpContext       http,
        CancellationToken ct = default) {
        var result = await _access.HasAccessAsync(null, new() {
            Operation = Operations.Update,
            Request   = request,
        }, http.User, ct);

        if (!result) {
            throw new AuthorizationException();
        }

        return true;
    }

    #endregion
}
