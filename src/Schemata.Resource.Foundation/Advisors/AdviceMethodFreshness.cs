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
///     Default order constants for <see cref="AdviceMethodFreshness{TEntity, TRequest, TResponse}" />.
/// </summary>
public static class AdviceMethodFreshness
{
    /// <summary>
    ///     Default order at <see cref="Orders.Base" /> -- parallel to
    ///     <see cref="AdviceUpdateFreshness{TEntity, TRequest}" />.
    /// </summary>
    public const int DefaultOrder = Orders.Base;
}

/// <summary>
///     Enforces optimistic concurrency for instance-scoped AIP-136 custom methods
///     per <seealso href="https://google.aip.dev/154">AIP-154: Resource freshness validation</seealso> by comparing the
///     request ETag with the loaded entity's <see cref="IConcurrency.Timestamp" />.
/// </summary>
/// <remarks>
///     <para>
///         The check fires whenever the request implements <see cref="IFreshness" /> and supplies a
///         non-empty ETag: any value that differs from the entity's current weak tag — including
///         strong-format or malformed tags — raises <see cref="AbortedException" /> (AIP-154:
///         a provided mismatching etag MUST abort). Only an absent or whitespace tag opts out.
///     </para>
///     <para>
///         Suppressed when <see cref="FreshnessSuppressed" /> is present.
///     </para>
/// </remarks>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TRequest">The request DTO type.</typeparam>
/// <typeparam name="TResponse">The custom method's response type.</typeparam>
public sealed class AdviceMethodFreshness<TEntity, TRequest, TResponse> : IResourceMethodAdvisor<TEntity, TRequest, TResponse>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
    where TResponse : class, ICanonicalName
{
    #region IResourceMethodAdvisor<TEntity,TRequest,TResponse> Members

    public int Order => AdviceMethodFreshness.DefaultOrder;

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
            throw new AbortedException();
        }

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
