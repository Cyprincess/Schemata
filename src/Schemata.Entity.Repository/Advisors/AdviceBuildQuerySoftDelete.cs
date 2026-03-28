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
    /// <summary>Default execution order.</summary>
    public const int DefaultOrder = Orders.Base;
}

/// <summary>
///     Applies a global query filter that excludes soft-deleted entities (where <see cref="ISoftDelete.DeleteTime" /> is
///     non-null).
/// </summary>
/// <typeparam name="TEntity">The entity type being queried.</typeparam>
/// <remarks>
///     <para>Order: 100,000,000.</para>
///     <para>
///         Auto-registered by
///         <see cref="Microsoft.Extensions.DependencyInjection.ServiceCollectionExtensions.AddRepository" />. Only
///         activates when <typeparamref name="TEntity" /> implements <see cref="ISoftDelete" />.
///     </para>
///     <para>Suppressed when <see cref="QuerySoftDeleteSuppressed" /> is present in the advice context.</para>
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

        container.ApplyModification(q => {
            return q.OfType<ISoftDelete>().Where(e => e.DeleteTime == null).OfType<TEntity>();
        });

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
