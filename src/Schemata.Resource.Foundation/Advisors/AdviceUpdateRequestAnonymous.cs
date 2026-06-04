using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Security.Skeleton;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Default order constants for <see cref="AdviceUpdateRequestAnonymous{TEntity,TRequest}" />.
/// </summary>
public static class AdviceUpdateRequestAnonymous
{
    /// <summary>
    ///     Default order at <see cref="Schemata.Abstractions.SchemataConstants.Orders.Base" /> — first
    ///     in the documented update request chain
    ///     (<c>anonymous → authorize → sanitize → validation</c>).
    /// </summary>
    public const int DefaultOrder = SchemataConstants.Orders.Base;
}

/// <summary>
///     Sets <see cref="AnonymousGranted" /> in the context when the update operation,
///     per <seealso href="https://google.aip.dev/134">AIP-134: Standard methods: Update</seealso>, is configured for
///     anonymous access via <see cref="AnonymousAccess" />.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TRequest">The request DTO type.</typeparam>
public sealed class AdviceUpdateRequestAnonymous<TEntity, TRequest> : IResourceUpdateRequestAdvisor<TEntity, TRequest>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
{
    #region IResourceUpdateRequestAdvisor<TEntity,TRequest> Members

    public int Order => AdviceUpdateRequestAnonymous.DefaultOrder;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext                     ctx,
        TRequest                          request,
        ResourceRequestContainer<TEntity> container,
        ClaimsPrincipal?                  principal,
        CancellationToken                 ct = default
    ) {
        if (AnonymousAccess.IsAnonymous<TEntity>(nameof(Operations.Update))) {
            ctx.Set(new AnonymousGranted());
        }

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
