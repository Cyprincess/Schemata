using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Security.Skeleton;

namespace Schemata.Resource.Foundation.Advisors;

public static class AdviceDeleteRequestAuthorize
{
    public const int DefaultOrder = AdviceDeleteRequestAnonymous.DefaultOrder + 10_000_000;
}

public sealed class AdviceDeleteRequestAuthorize<TEntity> : IResourceDeleteRequestAdvisor<TEntity>
    where TEntity : class, ICanonicalName
{
    private readonly IAccessProvider<TEntity, DeleteRequest>      _access;
    private readonly IEntitlementProvider<TEntity, DeleteRequest> _entitlement;

    public AdviceDeleteRequestAuthorize(
        IAccessProvider<TEntity, DeleteRequest>      access,
        IEntitlementProvider<TEntity, DeleteRequest> entitlement
    ) {
        _access      = access;
        _entitlement = entitlement;
    }

    #region IResourceDeleteRequestAdvisor<TEntity> Members

    /// <inheritdoc />
    public int Order => AdviceDeleteRequestAuthorize.DefaultOrder;

    /// <inheritdoc />
    public async Task<AdviseResult> AdviseAsync(
        AdviceContext                     ctx,
        DeleteRequest                     request,
        ResourceRequestContainer<TEntity> container,
        ClaimsPrincipal?                  principal,
        CancellationToken                 ct = default
    ) {
        var context = new AccessContext<DeleteRequest> { Operation = nameof(Operations.Delete), Request = request };

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
