using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Security.Skeleton;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Default order constants for <see cref="AdviceMethodRequestAuthorize{TEntity, TRequest}" />.
/// </summary>
public static class AdviceMethodRequestAuthorize
{
    /// <summary>
    ///     Default order: runs after
    ///     <see cref="AdviceMethodRequestAnonymous{TEntity, TRequest}" />.
    /// </summary>
    public const int DefaultOrder = AdviceMethodRequestAnonymous.DefaultOrder + 10_000_000;
}

/// <summary>
///     Authorizes AIP-136 custom method requests
///     per <seealso href="https://google.aip.dev/211">AIP-211: Authorization checks</seealso> via
///     <see cref="IAccessProvider{TEntity,TRequest}" />, with the verb (stashed
///     in <see cref="ResourceMethodVerb" />) supplied as the
///     <see cref="AccessContext{TRequest}.Operation" /> -- enabling fine-grained
///     per-verb permission policies.
///     Skips when <see cref="AnonymousGranted" /> is present.
///     Throws <see cref="AuthorizationException" /> on denial.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TRequest">The request DTO type.</typeparam>
public sealed class AdviceMethodRequestAuthorize<TEntity, TRequest> : IResourceMethodRequestAdvisor<TEntity, TRequest>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
{
    private readonly IAccessProvider<TEntity, TRequest> _access;

    /// <summary>
    ///     Initializes a new instance with the access provider.
    /// </summary>
    /// <param name="access">The custom-method access provider.</param>
    public AdviceMethodRequestAuthorize(IAccessProvider<TEntity, TRequest> access) { _access = access; }

    #region IResourceMethodRequestAdvisor<TEntity,TRequest> Members

    public int Order => AdviceMethodRequestAuthorize.DefaultOrder;

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

        if (!ctx.TryGet<ResourceMethodVerb>(out var marker) || marker is null) {
            return AdviseResult.Continue;
        }

        var context = new AccessContext<TRequest> { Operation = marker.Verb, Request = request };

        await AuthorizeHelper.EnsureAsync(_access, context, request.Name ?? string.Empty, principal, ct);

        return AdviseResult.Continue;
    }

    #endregion
}
