using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Entity.Repository.Advisors;

/// <summary>Order constants for <see cref="AdviceRemoveSoftDelete{TEntity}" />.</summary>
public static class AdviceRemoveSoftDelete
{
    /// <summary>Default execution order: 2,147,483,647.</summary>
    public const int DefaultOrder = Orders.Max;
}

/// <summary>
///     Converts a physical delete into a soft-delete by setting
///     <see cref="ISoftDelete.DeleteTime" /> and updating the entity instead of removing it,
///     per <seealso href="https://google.aip.dev/164">AIP-164: Soft delete</seealso> and
///     <seealso href="https://google.aip.dev/214">AIP-214: Resource expiration</seealso>.
/// </summary>
/// <typeparam name="TEntity">The entity type being removed.</typeparam>
/// <remarks>
///     Runs last in the remove pipeline. Returns <see cref="AdviseResult.Handle" /> to prevent
///     the physical delete. Only activates for entities implementing <see cref="ISoftDelete" />.
///     Suppressed by <see cref="SoftDeleteSuppressed" />.
/// </remarks>
public sealed class AdviceRemoveSoftDelete<TEntity> : IRepositoryRemoveAdvisor<TEntity>
    where TEntity : class
{
    #region IRepositoryRemoveAdvisor<TEntity> Members

    public int Order => AdviceRemoveSoftDelete.DefaultOrder;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext        ctx,
        IRepository<TEntity> repository,
        TEntity              entity,
        CancellationToken    ct
    ) {
        if (ctx.Has<SoftDeleteSuppressed>()) {
            return AdviseResult.Continue;
        }

        if (entity is not ISoftDelete trash) {
            return AdviseResult.Continue;
        }

        trash.DeleteTime = DateTime.UtcNow;

        // Persist as an update rather than a delete so the row is retained.
        await repository.UpdateAsync(entity, ct);

        // Handle signals the pipeline that the remove has been handled;
        // the entity will not be physically deleted.
        return AdviseResult.Handle;
    }

    #endregion
}
