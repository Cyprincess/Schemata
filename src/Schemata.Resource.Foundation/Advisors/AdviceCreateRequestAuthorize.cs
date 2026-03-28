using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Security.Skeleton;

namespace Schemata.Resource.Foundation.Advisors;

public static class AdviceCreateRequestAuthorize
{
    public const int DefaultOrder = AdviceCreateRequestAnonymous.DefaultOrder + 10_000_000;
}

public sealed class AdviceCreateRequestAuthorize<TEntity, TRequest> : IResourceCreateRequestAdvisor<TEntity, TRequest>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
{
    private readonly IAccessProvider<TEntity, TRequest> _access;

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
