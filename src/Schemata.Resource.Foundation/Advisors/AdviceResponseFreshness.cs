using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Default order constants for <see cref="AdviceResponseFreshness{TEntity,TDetail}" />.
/// </summary>
public static class AdviceResponseFreshness
{
    /// <summary>
    ///     Default order at <see cref="Orders.Base" />.
    /// </summary>
    public const int DefaultOrder = Orders.Base;
}

/// <summary>
///     Sets the ETag on response DTOs that implement <see cref="IFreshness" />
///     per <seealso href="https://google.aip.dev/154">AIP-154: Resource freshness validation</seealso>. Computes a weak ETag
///     from
///     the entity's <see cref="IConcurrency.Timestamp" />.
///     Suppressed when <see cref="FreshnessSuppressed" /> is present.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TDetail">The detail DTO type.</typeparam>
public class AdviceResponseFreshness<TEntity, TDetail> : IResourceResponseAdvisor<TEntity, TDetail>
    where TEntity : class, ICanonicalName
    where TDetail : class, ICanonicalName
{
    #region IResourceResponseAdvisor<TEntity,TDetail> Members

    /// <inheritdoc />
    public int Order => AdviceResponseFreshness.DefaultOrder;

    /// <inheritdoc />
    public Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        TEntity?          entity,
        TDetail?          detail,
        ClaimsPrincipal?  principal,
        CancellationToken ct = default
    ) {
        if (entity is null || detail is not IFreshness freshness) {
            return Task.FromResult(AdviseResult.Continue);
        }

        if (!FreshnessHelper.TryGetEntityTag(ctx, entity, out var tag)) {
            return Task.FromResult(AdviseResult.Continue);
        }

        freshness.EntityTag = tag;

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
