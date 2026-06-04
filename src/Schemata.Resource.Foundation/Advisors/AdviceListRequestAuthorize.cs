using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Security.Skeleton;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Default order constants for <see cref="AdviceListRequestAuthorize{TEntity}" />.
/// </summary>
public static class AdviceListRequestAuthorize
{
    /// <summary>
    ///     Default order: runs after <see cref="AdviceListRequestAnonymous{TEntity}" />.
    /// </summary>
    public const int DefaultOrder = AdviceListRequestAnonymous.DefaultOrder + 10_000_000;
}

/// <summary>
///     Authorizes list requests
///     per <seealso href="https://google.aip.dev/211">AIP-211: Authorization checks</seealso> and
///     <seealso href="https://google.aip.dev/132">AIP-132: Standard methods: List</seealso> via
///     <see cref="IAccessProvider{TEntity,ListRequest}" /> and applies row-level
///     entitlement filtering. Entitlement filtering is always applied; access
///     check is skipped when <see cref="AnonymousGranted" /> is present.
///     Throws <see cref="AuthorizationException" /> on denial.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
public sealed class AdviceListRequestAuthorize<TEntity> : IResourceListRequestAdvisor<TEntity>
    where TEntity : class, ICanonicalName
{
    private readonly IAccessProvider<TEntity, ListRequest>      _access;
    private readonly IEntitlementProvider<TEntity, ListRequest> _entitlement;

    /// <summary>
    ///     Initializes a new instance with access and entitlement providers.
    /// </summary>
    /// <param name="access">The <see cref="IAccessProvider{TEntity,ListRequest}" />.</param>
    /// <param name="entitlement">The <see cref="IEntitlementProvider{TEntity,ListRequest}" />.</param>
    public AdviceListRequestAuthorize(
        IAccessProvider<TEntity, ListRequest>      access,
        IEntitlementProvider<TEntity, ListRequest> entitlement
    ) {
        _access      = access;
        _entitlement = entitlement;
    }

    #region IResourceListRequestAdvisor<TEntity> Members

    public int Order => AdviceListRequestAuthorize.DefaultOrder;

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

        await AuthorizeHelper.EnsureAsync(_access, context, request.Parent ?? string.Empty, principal, ct);

        return AdviseResult.Continue;
    }

    #endregion
}
