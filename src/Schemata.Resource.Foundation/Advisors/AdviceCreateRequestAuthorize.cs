using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Security.Skeleton;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Default order constants for <see cref="AdviceCreateRequestAuthorize{TEntity,TRequest}" />.
/// </summary>
public static class AdviceCreateRequestAuthorize
{
    /// <summary>
    ///     Default order: runs after <see cref="AdviceCreateRequestAnonymous{TEntity,TRequest}" />.
    /// </summary>
    public const int DefaultOrder = AdviceCreateRequestAnonymous.DefaultOrder + 10_000_000;
}

/// <summary>
///     Authorizes create requests
///     per <seealso href="https://google.aip.dev/211">AIP-211: Authorization checks</seealso> and
///     <seealso href="https://google.aip.dev/133">AIP-133: Standard methods: Create</seealso> via
///     <see cref="IAccessProvider{TEntity,TRequest}" />.
///     Skips when <see cref="AnonymousGranted" /> is present.
///     Throws <see cref="AuthorizationException" /> on denial.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TRequest">The request DTO type.</typeparam>
public sealed class AdviceCreateRequestAuthorize<TEntity, TRequest> : IResourceCreateRequestAdvisor<TEntity, TRequest>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
{
    private readonly IAccessProvider<TEntity, TRequest> _access;

    /// <summary>
    ///     Initializes a new instance with the access provider.
    /// </summary>
    /// <param name="access">The <see cref="IAccessProvider{TEntity,TRequest}" />.</param>
    public AdviceCreateRequestAuthorize(IAccessProvider<TEntity, TRequest> access) { _access = access; }

    #region IResourceCreateRequestAdvisor<TEntity,TRequest> Members

    /// <inheritdoc />
    public int Order => AdviceCreateRequestAuthorize.DefaultOrder;

    /// <inheritdoc />
    public async Task<AdviseResult> AdviseAsync(
        AdviceContext                     ctx,
        TRequest                          request,
        ResourceRequestContainer<TEntity> container,
        ClaimsPrincipal?                  principal,
        CancellationToken                 ct = default
    ) {
        if (ctx.Has<AnonymousGranted>()) {
            return AdviseResult.Continue;
        }

        var context = new AccessContext<TRequest> { Operation = nameof(Operations.Create), Request = request };

        var result = await _access.HasAccessAsync(null, context, principal, ct);
        if (!result) {
            throw new AuthorizationException();
        }

        return AdviseResult.Continue;
    }

    #endregion
}
