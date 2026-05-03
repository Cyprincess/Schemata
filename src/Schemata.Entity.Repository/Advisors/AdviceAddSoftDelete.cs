using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Entity.Repository.Advisors;

/// <summary>Order constants for <see cref="AdviceAddSoftDelete{TEntity}" />.</summary>
public static class AdviceAddSoftDelete
{
    /// <summary>Default execution order: 2,147,483,647.</summary>
    public const int DefaultOrder = Orders.Max;
}

/// <summary>
///     Clears <see cref="ISoftDelete.DeleteTime" /> to <see langword="null" /> when a
///     soft-deletable entity is added, ensuring it is not marked as deleted, per
///     <seealso href="https://google.aip.dev/164">AIP-164: Soft delete</seealso>.
/// </summary>
/// <typeparam name="TEntity">The entity type being added.</typeparam>
/// <remarks>
///     Runs last in the add pipeline (after validation). Only activates for entities
///     implementing <see cref="ISoftDelete" />. Suppressed by
///     <see cref="SoftDeleteSuppressed" />.
/// </remarks>
public sealed class AdviceAddSoftDelete<TEntity> : IRepositoryAddAdvisor<TEntity>
    where TEntity : class
{
    #region IRepositoryAddAdvisor<TEntity> Members

    /// <inheritdoc />
    public int Order => AdviceAddSoftDelete.DefaultOrder;

    /// <inheritdoc />
    public Task<AdviseResult> AdviseAsync(
        AdviceContext        ctx,
        IRepository<TEntity> repository,
        TEntity              entity,
        CancellationToken    ct
    ) {
        if (ctx.Has<SoftDeleteSuppressed>()) {
            return Task.FromResult(AdviseResult.Continue);
        }

        if (entity is not ISoftDelete trash) {
            return Task.FromResult(AdviseResult.Continue);
        }

        trash.DeleteTime = null;

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
