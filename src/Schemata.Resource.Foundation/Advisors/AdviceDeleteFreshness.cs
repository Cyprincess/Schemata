using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Default order constants for <see cref="AdviceDeleteFreshness{TEntity}" />.
/// </summary>
public static class AdviceDeleteFreshness
{
    /// <summary>
    ///     Default order at <see cref="Orders.Base" />.
    /// </summary>
    public const int DefaultOrder = Orders.Base;
}

/// <summary>
///     Enforces optimistic concurrency for delete operations
///     per <seealso href="https://google.aip.dev/154">AIP-154: Resource freshness validation</seealso> by comparing the
///     request ETag with the entity's concurrency timestamp.
/// </summary>
/// <remarks>
///     <para>
///         The check fires whenever <see cref="DeleteRequest.Etag" /> is non-empty: any value that
///         differs from the entity's current weak tag — including strong-format or malformed tags —
///         raises <see cref="ConcurrencyException" /> (AIP-154: a provided mismatching etag MUST
///         abort). Only an absent or whitespace tag opts out.
///     </para>
///     <para>
///         Suppressed when <see cref="FreshnessSuppressed" /> is present.
///     </para>
/// </remarks>
/// <typeparam name="TEntity">The entity type.</typeparam>
public sealed class AdviceDeleteFreshness<TEntity> : IResourceDeleteAdvisor<TEntity>
    where TEntity : class, ICanonicalName
{
    #region IResourceDeleteAdvisor<TEntity> Members

    public int Order => AdviceDeleteFreshness.DefaultOrder;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        DeleteRequest     request,
        TEntity           entity,
        ClaimsPrincipal?  principal,
        CancellationToken ct = default
    ) {
        if (!FreshnessHelper.TryGetEntityTag(ctx, entity, out var expected)) {
            return Task.FromResult(AdviseResult.Continue);
        }

        var tag = request.Etag;

        if (string.IsNullOrWhiteSpace(tag)) {
            return Task.FromResult(AdviseResult.Continue);
        }

        if (tag != expected) {
            throw new ConcurrencyException();
        }

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
