using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Resource.Foundation.Advisors;

public static class AdviceDeleteFreshness
{
    public const int DefaultOrder = Orders.Base;
}

/// <summary>
///     Enforces optimistic concurrency for delete operations by comparing the request ETag with the entity timestamp.
/// </summary>
/// <typeparam name="TEntity">The entity type being deleted.</typeparam>
/// <remarks>
///     Order: 200,000,000. Auto-registered by <see cref="Features.SchemataResourceFeature" />.
///     Skipped when the <see cref="Schemata.Abstractions.Resource.DeleteRequest.Force" /> flag is set on the delete
///     request.
///     Throws <see cref="Schemata.Abstractions.Exceptions.ConcurrencyException" /> when the ETag does not match.
///     Suppressed when <see cref="FreshnessSuppressed" /> is present in the advice context.
/// </remarks>
public sealed class AdviceDeleteFreshness<TEntity> : IResourceDeleteAdvisor<TEntity>
    where TEntity : class, ICanonicalName
{
    #region IResourceDeleteAdvisor<TEntity> Members

    /// <inheritdoc />
    public int Order => AdviceDeleteFreshness.DefaultOrder;

    /// <inheritdoc />
    public Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        TEntity           entity,
        DeleteRequest     request,
        ClaimsPrincipal?  principal,
        CancellationToken ct = default
    ) {
        if (request.Force) {
            return Task.FromResult(AdviseResult.Continue);
        }

        if (!FreshnessHelper.TryGetEntityTag(ctx, entity, out var expected)) {
            return Task.FromResult(AdviseResult.Continue);
        }

        var tag = request.Etag;

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
