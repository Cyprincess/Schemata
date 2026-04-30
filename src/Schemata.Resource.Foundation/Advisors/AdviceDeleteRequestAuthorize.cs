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
///     Default order constants for <see cref="AdviceDeleteRequestAuthorize{TEntity}" />.
/// </summary>
public static class AdviceDeleteRequestAuthorize
{
    /// <summary>
    ///     Default order: runs after <see cref="AdviceDeleteRequestAnonymous{TEntity}" />.
    /// </summary>
    public const int DefaultOrder = AdviceDeleteRequestAnonymous.DefaultOrder + 10_000_000;
}

/// <summary>
///     Authorizes delete requests
///     per <seealso href="https://google.aip.dev/211">AIP-211: Authorization checks</seealso> and
///     <seealso href="https://google.aip.dev/135">AIP-135: Standard methods: Delete</seealso> via
///     <see cref="IAccessProvider{TEntity,DeleteRequest}" /> and applies row-level
///     entitlement filtering. Entitlement filtering is always applied; access
///     check is skipped when <see cref="AnonymousGranted" /> is present.
///     Throws <see cref="AuthorizationException" /> on denial.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
public sealed class AdviceDeleteRequestAuthorize<TEntity> : IResourceDeleteRequestAdvisor<TEntity>
    where TEntity : class, ICanonicalName
{
    private readonly IAccessProvider<TEntity, DeleteRequest>      _access;
    private readonly IEntitlementProvider<TEntity, DeleteRequest> _entitlement;

    /// <summary>
    ///     Initializes a new instance with access and entitlement providers.
    /// </summary>
    /// <param name="access">The <see cref="IAccessProvider{TEntity,DeleteRequest}" />.</param>
    /// <param name="entitlement">The <see cref="IEntitlementProvider{TEntity,DeleteRequest}" />.</param>
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

        await AuthorizeHelper.EnsureAsync(_access, context, request.Name ?? string.Empty, principal, ct);

        return AdviseResult.Continue;
    }

    #endregion
}
