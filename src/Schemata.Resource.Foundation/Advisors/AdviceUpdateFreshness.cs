using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Common.Errors;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Default order constants for <see cref="AdviceUpdateFreshness{TEntity,TRequest}" />.
/// </summary>
public static class AdviceUpdateFreshness
{
    /// <summary>
    ///     Default order: chained after <see cref="AdviceUpdateSoftDeleted" /> so the
    ///     soft-delete guard rejects deleted entities before any concurrency comparison.
    /// </summary>
    public const int DefaultOrder = AdviceUpdateSoftDeleted.DefaultOrder + 10_000_000;
}

/// <summary>
///     Enforces optimistic concurrency for update operations
///     per <seealso href="https://google.aip.dev/154">AIP-154: Resource freshness validation</seealso> by comparing the
///     request ETag with the entity's concurrency timestamp.
/// </summary>
/// <remarks>
///     <para>
///         The check fires whenever the request implements <see cref="IFreshness" /> and supplies a
///         non-empty ETag: any value that differs from the entity's current weak tag — including
///         strong-format or malformed tags — raises <see cref="FailedPreconditionException" />
///         with <c>ETAG_MISMATCH</c> precondition subject. Only an absent or whitespace tag opts out.
///     </para>
///     <para>
///         Suppressed when <see cref="FreshnessSuppressed" /> is present.
///     </para>
/// </remarks>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TRequest">The request DTO type.</typeparam>
public sealed class AdviceUpdateFreshness<TEntity, TRequest> : IResourceUpdateAdvisor<TEntity, TRequest>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
{
    #region IResourceUpdateAdvisor<TEntity,TRequest> Members

    public int Order => AdviceUpdateFreshness.DefaultOrder;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        TRequest          request,
        TEntity           entity,
        ClaimsPrincipal?  principal,
        CancellationToken ct = default
    ) {
        if (!FreshnessHelper.TryGetEntityTag(ctx, entity, out var expected)) {
            return Task.FromResult(AdviseResult.Continue);
        }

        var tag = request is IFreshness freshness ? freshness.EntityTag : null;

        if (string.IsNullOrWhiteSpace(tag)) {
            return Task.FromResult(AdviseResult.Continue);
        }

        if (tag != expected) {
            throw SchemataResourceErrors.PreconditionFailed<TEntity>(
                name: entity.CanonicalName,
                subject: PreconditionSubjects.EtagMismatch);
        }

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
