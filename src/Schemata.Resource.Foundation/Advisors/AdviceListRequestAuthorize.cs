using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Security.Skeleton;

namespace Schemata.Resource.Foundation.Advisors;

public static class AdviceListRequestAuthorize
{
    public const int DefaultOrder = AdviceListRequestAnonymous.DefaultOrder + 10_000_000;
}

public sealed class AdviceListRequestAuthorize<TEntity> : IResourceListRequestAdvisor<TEntity>
    where TEntity : class, ICanonicalName
{
    private readonly IAccessProvider<TEntity, ListRequest>      _access;
    private readonly IEntitlementProvider<TEntity, ListRequest> _entitlement;

    public AdviceListRequestAuthorize(
        IAccessProvider<TEntity, ListRequest>      access,
        IEntitlementProvider<TEntity, ListRequest> entitlement
    ) {
        _access      = access;
        _entitlement = entitlement;
    }

    #region IResourceListRequestAdvisor<TEntity> Members

    /// <inheritdoc />
    public int Order => AdviceListRequestAuthorize.DefaultOrder;

    /// <inheritdoc />
    public async Task<AdviseResult> AdviseAsync(
        AdviceContext                     ctx,
        ListRequest                       request,
        ResourceRequestContainer<TEntity> container,
        ClaimsPrincipal?                  principal,
        CancellationToken                 ct = default
    ) {
        var context = new AccessContext<ListRequest> { Operation = nameof(Operations.List), Request = request };

        var expression = await _entitlement.GenerateEntitlementExpressionAsync(context, principal, ct);
        container.ApplyModification(expression);

        if (ctx.Has<AnonymousGranted>()) {
            return AdviseResult.Continue;
        }

        var result = await _access.HasAccessAsync(null, context, principal, ct);
        if (!result) {
            throw new AuthorizationException();
        }

        return AdviseResult.Continue;
    }

    #endregion
}
