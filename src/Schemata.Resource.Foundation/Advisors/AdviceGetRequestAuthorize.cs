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
///     Default order constants for <see cref="AdviceGetRequestAuthorize{TEntity}" />.
/// </summary>
public static class AdviceGetRequestAuthorize
{
    /// <summary>
    ///     Default order: runs after <see cref="AdviceGetRequestAnonymous{TEntity}" />.
    /// </summary>
    public const int DefaultOrder = AdviceGetRequestAnonymous.DefaultOrder + 10_000_000;
}

/// <summary>
///     Authorizes get requests
///     per <seealso href="https://google.aip.dev/211">AIP-211: Authorization checks</seealso> and
///     <seealso href="https://google.aip.dev/131">AIP-131: Standard methods: Get</seealso> via
///     <see cref="IAccessProvider{TEntity,GetRequest}" /> and applies row-level
///     entitlement filtering. Entitlement filtering is always applied; access
///     check is skipped when <see cref="AnonymousGranted" /> is present.
///     Throws <see cref="AuthorizationException" /> on denial.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
public sealed class AdviceGetRequestAuthorize<TEntity> : IResourceGetRequestAdvisor<TEntity>
    where TEntity : class, ICanonicalName
{
    private readonly IAccessProvider<TEntity, GetRequest>      _access;
    private readonly IEntitlementProvider<TEntity, GetRequest> _entitlement;

    /// <summary>
    ///     Initializes a new instance with access and entitlement providers.
    /// </summary>
    /// <param name="access">The <see cref="IAccessProvider{TEntity,GetRequest}" />.</param>
    /// <param name="entitlement">The <see cref="IEntitlementProvider{TEntity,GetRequest}" />.</param>
    public AdviceGetRequestAuthorize(
        IAccessProvider<TEntity, GetRequest>      access,
        IEntitlementProvider<TEntity, GetRequest> entitlement
    ) {
        _access      = access;
        _entitlement = entitlement;
    }

    #region IResourceGetRequestAdvisor<TEntity> Members

    /// <inheritdoc />
    public int Order => AdviceGetRequestAuthorize.DefaultOrder;

    /// <inheritdoc />
    public async Task<AdviseResult> AdviseAsync(
        AdviceContext                     ctx,
        GetRequest                        request,
        ResourceRequestContainer<TEntity> container,
        ClaimsPrincipal?                  principal,
        CancellationToken                 ct = default
    ) {
        var context = new AccessContext<GetRequest> { Operation = nameof(Operations.Get), Request = request };

        var expression = await _entitlement.GenerateEntitlementExpressionAsync(context, principal, ct);
        container.ApplyModification(expression);

        if (ctx.Has<AnonymousGranted>()) {
            return AdviseResult.Continue;
        }

        await AuthorizeHelper.EnsureAsync(_access, context, request.Name ?? string.Empty, principal, ct);

        return AdviseResult.Continue;
    }

    #endregion
}
