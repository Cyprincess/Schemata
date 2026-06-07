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
    ///     Default order at <see cref="Orders.Base" /> ── parallel to
    ///     <see cref="AdviceUpdateFreshness{TEntity, TRequest}" />.
    /// </summary>
    public const int DefaultOrder = Orders.Base;
}

/// <summary>
///     Compares the inbound weak ETag on an AIP-136 custom method request
///     against the entity's current tag per
///     <seealso href="https://google.aip.dev/154">AIP-154: Resource freshness validation</seealso>.
/// </summary>
/// <remarks>
///     <para>
///         When the request implements <see cref="IFreshness" /> and supplies a
///         <c>W/</c>-prefixed <c>EntityTag</c>, the value must match the tag
///         computed from the loaded entity. Missing, empty, or non-<c>W/</c>
///         tags are treated as opt-out: the request proceeds without
///         freshness validation.
///     </para>
///     <para>
///         Throws <see cref="ConcurrencyException" /> on tag mismatch.
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
