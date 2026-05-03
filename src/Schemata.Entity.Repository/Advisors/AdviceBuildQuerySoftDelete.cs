using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Entity.Repository.Advisors;

/// <summary>Order constants for <see cref="AdviceBuildQuerySoftDelete{TEntity}" />.</summary>
public static class AdviceBuildQuerySoftDelete
{
    /// <summary>Default execution order: 100,000,000.</summary>
    public const int DefaultOrder = Orders.Base;
}

/// <summary>
///     Applies a global query filter that excludes soft-deleted entities
///     (where <see cref="ISoftDelete.DeleteTime" /> is non-null), per
///     <seealso href="https://google.aip.dev/160">AIP-160: Filtering</seealso> and
///     <seealso href="https://google.aip.dev/164">AIP-164: Soft delete</seealso>.
/// </summary>
/// <typeparam name="TEntity">The entity type being queried.</typeparam>
/// <remarks>
///     Only activates when <typeparamref name="TEntity" /> implements <see cref="ISoftDelete" />.
///     Suppressed by <see cref="QuerySoftDeleteSuppressed" />.
/// </remarks>
public sealed class AdviceBuildQuerySoftDelete<TEntity> : IRepositoryBuildQueryAdvisor<TEntity>
    where TEntity : class
{
    #region IRepositoryBuildQueryAdvisor<TEntity> Members

    /// <inheritdoc />
    public int Order => AdviceBuildQuerySoftDelete.DefaultOrder;

    /// <inheritdoc />
    public Task<AdviseResult> AdviseAsync(
        AdviceContext           ctx,
        QueryContainer<TEntity> container,
        CancellationToken       ct = default
    ) {
        if (ctx.Has<QuerySoftDeleteSuppressed>()) {
            return Task.FromResult(AdviseResult.Continue);
        }

        if (!typeof(ISoftDelete).IsAssignableFrom(typeof(TEntity))) {
            return Task.FromResult(AdviseResult.Continue);
        }

        // Cast through ISoftDelete to apply the filter, then back to TEntity.
        // This is necessary because the C# compiler cannot prove TEntity : ISoftDelete
        // at compile time, so we use runtime type-checking via OfType.
        container.ApplyModification(q => {
            return q.OfType<ISoftDelete>().Where(e => e.DeleteTime == null).OfType<TEntity>();
        });

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
