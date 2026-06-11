using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;

namespace Schemata.Resource.Foundation.Advisors;

/// <summary>
///     Default order constants for <see cref="AdviceUpdateSoftDeleted{TEntity, TRequest}" />.
/// </summary>
public static class AdviceUpdateSoftDeleted
{
    /// <summary>
    ///     Default order: runs before <see cref="AdviceUpdateFreshness{TEntity, TRequest}" />
    ///     so a soft-deleted entity is rejected before any concurrency comparison.
    /// </summary>
    public const int DefaultOrder = AdviceUpdateFreshness.DefaultOrder - 10_000_000;
}

/// <summary>
///     Rejects updates to soft-deleted entities
///     per <seealso href="https://google.aip.dev/164">AIP-164: Soft delete</seealso>: a resource marked
///     deleted must be restored before it can be mutated.
///     Throws <see cref="FailedPreconditionException" /> when the loaded entity carries a
///     <see cref="ISoftDelete.DeleteTime" />.
///     Suppressed when <see cref="SoftDeleteGuardSuppressed" /> is present.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TRequest">The request DTO type.</typeparam>
public sealed class AdviceUpdateSoftDeleted<TEntity, TRequest> : IResourceUpdateAdvisor<TEntity, TRequest>
    where TEntity : class, ICanonicalName
    where TRequest : class, ICanonicalName
{
    #region IResourceUpdateAdvisor<TEntity,TRequest> Members

    public int Order => AdviceUpdateSoftDeleted.DefaultOrder;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext     ctx,
        TRequest          request,
        TEntity           entity,
        ClaimsPrincipal?  principal,
        CancellationToken ct = default
    ) {
        if (ctx.Has<SoftDeleteGuardSuppressed>()) {
            return Task.FromResult(AdviseResult.Continue);
        }

        if (entity is ISoftDelete { DeleteTime: not null }) {
            throw new FailedPreconditionException(
                message: $"Resource '{entity.CanonicalName}' is deleted and must be undeleted before it can be updated.");
        }

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
