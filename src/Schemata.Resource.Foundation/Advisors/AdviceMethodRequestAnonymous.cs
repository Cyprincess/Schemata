using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Security.Skeleton;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Default order constants for <see cref="AdviceMethodRequestAnonymous{TEntity, TRequest}" />.
/// </summary>
public static class AdviceMethodRequestAnonymous
{
    /// <summary>
    ///     Default order at <see cref="SchemataConstants.Orders.Base" /> -- first
    ///     in the documented custom-method request chain
    ///     (<c>anonymous -> authorize -> idempotency</c>).
    /// </summary>
    public const int DefaultOrder = SchemataConstants.Orders.Base;
}

/// <summary>
///     Sets <see cref="AnonymousGranted" /> in the context when the AIP-136 custom
///     method, keyed by the verb stashed in
///     <see cref="ResourceMethodVerb" />, is configured for anonymous access via
///     <see cref="AnonymousAccess" />.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TRequest">The request DTO type.</typeparam>
public sealed class AdviceMethodRequestAnonymous<TEntity, TRequest> : IResourceMethodRequestAdvisor<TEntity, TRequest>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
{
    #region IResourceMethodRequestAdvisor<TEntity,TRequest> Members

    public int Order => AdviceMethodRequestAnonymous.DefaultOrder;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext                     ctx,
        TRequest                          request,
        ResourceRequestContainer<TEntity> container,
        ClaimsPrincipal?                  principal,
        CancellationToken                 ct = default
    ) {
        if (!ctx.TryGet<ResourceMethodVerb>(out var marker) || marker is null) {
            return Task.FromResult(AdviseResult.Continue);
        }

        if (AnonymousAccess.IsAnonymous<TEntity>(marker.Verb)) {
            ctx.Set(new AnonymousGranted());
        }

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
