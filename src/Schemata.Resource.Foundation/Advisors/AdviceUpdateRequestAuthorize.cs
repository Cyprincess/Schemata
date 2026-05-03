using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Security.Skeleton;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Default order constants for <see cref="AdviceUpdateRequestAuthorize{TEntity,TRequest}" />.
/// </summary>
public static class AdviceUpdateRequestAuthorize
{
    /// <summary>
    ///     Default order: runs after <see cref="AdviceUpdateRequestAnonymous{TEntity,TRequest}" />.
    /// </summary>
    public const int DefaultOrder = AdviceUpdateRequestAnonymous.DefaultOrder + 10_000_000;
}

/// <summary>
///     Authorizes update requests
///     per <seealso href="https://google.aip.dev/211">AIP-211: Authorization checks</seealso> and
///     <seealso href="https://google.aip.dev/134">AIP-134: Standard methods: Update</seealso> via
///     <see cref="IAccessProvider{TEntity,TRequest}" /> and applies row-level
///     entitlement filtering via
///     <see cref="IEntitlementProvider{TEntity,TRequest}" />.
///     Skips access check when <see cref="AnonymousGranted" /> is present.
///     Entitlement filtering is always applied regardless of anonymous access.
///     Throws <see cref="AuthorizationException" /> on denial.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TRequest">The request DTO type.</typeparam>
public sealed class AdviceUpdateRequestAuthorize<TEntity, TRequest> : IResourceUpdateRequestAdvisor<TEntity, TRequest>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
{
    private readonly IAccessProvider<TEntity, TRequest>      _access;
    private readonly IEntitlementProvider<TEntity, TRequest> _entitlement;

    /// <summary>
    ///     Initializes a new instance with access and entitlement providers.
    /// </summary>
    /// <param name="access">The <see cref="IAccessProvider{TEntity,TRequest}" />.</param>
    /// <param name="entitlement">The <see cref="IEntitlementProvider{TEntity,TRequest}" />.</param>
    public AdviceUpdateRequestAuthorize(
        IAccessProvider<TEntity, TRequest>      access,
        IEntitlementProvider<TEntity, TRequest> entitlement
    ) {
        _access      = access;
        _entitlement = entitlement;
    }

    #region IResourceUpdateRequestAdvisor<TEntity,TRequest> Members

    /// <inheritdoc />
    public int Order => AdviceUpdateRequestAuthorize.DefaultOrder;

    /// <inheritdoc />
    public async Task<AdviseResult> AdviseAsync(
        AdviceContext                     ctx,
        TRequest                          request,
        ResourceRequestContainer<TEntity> container,
        ClaimsPrincipal?                  principal,
        CancellationToken                 ct = default
    ) {
        var context = new AccessContext<TRequest> { Operation = nameof(Operations.Update), Request = request };

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
