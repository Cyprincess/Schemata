using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Resource.Foundation.Advisors;

public static class AdviceResponseFreshness
{
    public const int DefaultOrder = Orders.Base;
}

/// <summary>
///     Sets the ETag on response detail DTOs that implement <see cref="Schemata.Abstractions.Resource.IFreshness" />.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TDetail">The detail DTO type returned to the caller.</typeparam>
/// <remarks>
///     Order: 100,000,000. Auto-registered by <see cref="Features.SchemataResourceFeature" />.
///     Computes a weak ETag from the entity's <see cref="Schemata.Abstractions.Entities.IConcurrency.Timestamp" />
///     and assigns it to the detail's <see cref="Schemata.Abstractions.Resource.IFreshness.EntityTag" /> property.
///     Suppressed when <see cref="FreshnessSuppressed" /> is present in the advice context.
/// </remarks>
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
