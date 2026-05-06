using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Security.Skeleton;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Default order constants for <see cref="AdviceGetRequestAnonymous{TEntity}" />.
/// </summary>
public static class AdviceGetRequestAnonymous
{
    /// <summary>
    ///     Default order at <see cref="Orders.Base" />.
    /// </summary>
    public const int DefaultOrder = Orders.Base;
}

/// <summary>
///     Sets <see cref="AnonymousGranted" /> in the context when the get operation,
///     per <seealso href="https://google.aip.dev/131">AIP-131: Standard methods: Get</seealso>, is configured for
///     anonymous access via <see cref="AnonymousAccess" />.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
public sealed class AdviceGetRequestAnonymous<TEntity> : IResourceGetRequestAdvisor<TEntity>
    where TEntity : class, ICanonicalName
{
    #region IResourceGetRequestAdvisor<TEntity> Members

    public int Order => AdviceGetRequestAnonymous.DefaultOrder;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext                     ctx,
        GetRequest                        request,
        ResourceRequestContainer<TEntity> container,
        ClaimsPrincipal?                  principal,
        CancellationToken                 ct = default
    ) {
        if (AnonymousAccess.IsAnonymous<TEntity>(nameof(Operations.Get))) {
            ctx.Set(new AnonymousGranted());
        }

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
