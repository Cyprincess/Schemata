using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Schemata.Abstractions.Advices;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Security.Skeleton;

namespace Schemata.Resource.Foundation.Advices;

public sealed class AdviceListRequestAuthorize<TEntity> : IResourceListRequestAdvice<TEntity>
    where TEntity : class, IIdentifier
{
    private readonly IAccessProvider<TEntity, ResourceRequestContext<ListRequest>>      _access;
    private readonly IEntitlementProvider<TEntity, ResourceRequestContext<ListRequest>> _entitlement;

    public AdviceListRequestAuthorize(
        IAccessProvider<TEntity, ResourceRequestContext<ListRequest>>      access,
        IEntitlementProvider<TEntity, ResourceRequestContext<ListRequest>> entitlement) {
        _access      = access;
        _entitlement = entitlement;
    }

    #region IResourceListRequestAdvice<TEntity> Members

    public int Order => 100_000_000;

    public int Priority => Order;

    public async Task<bool> AdviseAsync(
        AdviceContext                     ctx,
        ListRequest                       request,
        ResourceRequestContainer<TEntity> container,
        HttpContext                       http,
        CancellationToken                 ct = default) {
        var context = new ResourceRequestContext<ListRequest> {
            Operation = Operations.List,
            Request   = request,
        };

        var result = await _access.HasAccessAsync(null, context, http.User, ct);

        if (!result) {
            throw new AuthorizationException();
        }

        var expression = await _entitlement.GenerateEntitlementExpressionAsync(context, http.User, ct);
        if (expression is not null) {
            container.ApplyModification(expression);
        }

        return true;
    }

    #endregion
}
