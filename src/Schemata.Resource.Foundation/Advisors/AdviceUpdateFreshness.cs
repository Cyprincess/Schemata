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
///     Default order constants for <see cref="AdviceUpdateFreshness{TEntity,TRequest}" />.
/// </summary>
public static class AdviceUpdateFreshness
{
    /// <summary>
    ///     Default order at <see cref="Orders.Base" />.
    /// </summary>
    public const int DefaultOrder = Orders.Base;
}

/// <summary>
///     Enforces optimistic concurrency for update operations
///     per <seealso href="https://google.aip.dev/154">AIP-154: Resource freshness validation</seealso> by comparing the
///     request
///     ETag with the entity's concurrency timestamp.
///     The request must implement <see cref="IFreshness" /> and the ETag must start with
///     <c>W/</c>; if it doesn't match, throws <see cref="ConcurrencyException" />.
///     Suppressed when <see cref="FreshnessSuppressed" /> is present.
/// </summary>
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

        if (string.IsNullOrWhiteSpace(tag) || !tag.StartsWith("W/")) {
            return Task.FromResult(AdviseResult.Continue);
        }

        if (tag != expected) {
            throw new ConcurrencyException();
        }

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
