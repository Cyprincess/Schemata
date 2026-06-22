using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Security.Foundation;
using Schemata.Security.Skeleton;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Default order constants for <see cref="AdviceCreateRequestAnonymous{TEntity,TRequest}" />.
/// </summary>
public static class AdviceCreateRequestAnonymous
{
    /// <summary>
    ///     Default order at <see cref="Schemata.Abstractions.SchemataConstants.Orders.Base" /> — first
    ///     in the documented create request chain
    ///     (<c>anonymous → authorize → sanitize → validation → idempotency</c>).
    /// </summary>
    public const int DefaultOrder = SchemataConstants.Orders.Base;
}

/// <summary>
///     Sets <see cref="AnonymousGranted" /> in the context when the create operation,
///     per <seealso href="https://google.aip.dev/133">AIP-133: Standard methods: Create</seealso>, is configured for
///     anonymous access via <see cref="AnonymousAccess" />.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TRequest">The request DTO type.</typeparam>
public sealed class AdviceCreateRequestAnonymous<TEntity, TRequest> : IResourceCreateRequestAdvisor<TEntity, TRequest>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
{
    #region IResourceCreateRequestAdvisor<TEntity,TRequest> Members

    public int Order => AdviceCreateRequestAnonymous.DefaultOrder;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext                     ctx,
        TRequest                          request,
        ResourceRequestContainer<TEntity> container,
        ClaimsPrincipal?                  principal,
        CancellationToken                 ct = default
    ) {
        if (AnonymousAccess.IsAnonymous<TEntity>(nameof(Operations.Create))) {
            ctx.Set(new AnonymousGranted());
        }

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
