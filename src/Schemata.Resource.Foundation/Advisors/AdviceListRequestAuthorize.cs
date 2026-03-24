using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Security.Skeleton;

namespace Schemata.Resource.Foundation.Advisors;

public static class AdviceListRequestAuthorize
{
    public const int DefaultOrder = SchemataConstants.Orders.Base;
}

/// <summary>
/// Authorizes list requests and applies entitlement-based query filtering for the current user.
/// </summary>
/// <typeparam name="TEntity">The entity type being listed.</typeparam>
/// <remarks>
/// Order: 100,000,000. Registered by <see cref="SchemataResourceBuilder.WithAuthorization"/>;
/// not auto-registered by <see cref="Features.SchemataResourceFeature"/>.
/// Skips authorization when the entity is decorated with <see cref="Schemata.Abstractions.Resource.AnonymousAttribute"/> for the List operation.
/// After verifying access, applies entitlement expressions to the query container so results
/// are scoped to what the user is allowed to see.
/// Throws <see cref="Schemata.Abstractions.Exceptions.AuthorizationException"/> if access is denied.
/// </remarks>
public sealed class AdviceListRequestAuthorize<TEntity> : IResourceListRequestAdvisor<TEntity>
    where TEntity : class, ICanonicalName
{
    private readonly IAccessProvider<TEntity, ResourceRequestContext<ListRequest>>      _access;
    private readonly IEntitlementProvider<TEntity, ResourceRequestContext<ListRequest>> _entitlement;

    public AdviceListRequestAuthorize(
        IAccessProvider<TEntity, ResourceRequestContext<ListRequest>>      access,
        IEntitlementProvider<TEntity, ResourceRequestContext<ListRequest>> entitlement
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
        HttpContext?                      http,
        CancellationToken                 ct = default
    ) {
        if (AnonymousAccessHelper.IsAnonymous<TEntity>(Operations.List)) {
            return AdviseResult.Continue;
        }

        var context = new ResourceRequestContext<ListRequest> { Operation = Operations.List, Request = request };

        var result = await _access.HasAccessAsync(null, context, http?.User, ct);

        if (!result) {
            throw new AuthorizationException();
        }

        var expression = await _entitlement.GenerateEntitlementExpressionAsync(context, http?.User, ct);
        if (expression is not null) {
            container.ApplyModification(expression);
        }

        return AdviseResult.Continue;
    }

    #endregion
}
