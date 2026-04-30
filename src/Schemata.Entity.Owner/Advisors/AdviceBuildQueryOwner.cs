using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;

namespace Schemata.Entity.Owner.Advisors;

/// <summary>Order constants for <see cref="AdviceBuildQueryOwner{TEntity}" />.</summary>
public static class AdviceBuildQueryOwner
{
    /// <summary>Default execution order.</summary>
    public const int DefaultOrder = AdviceBuildQuerySoftDelete.DefaultOrder + 10_000_000;
}

/// <summary>
///     Applies a global query filter that restricts results to entities owned by the current caller, as
///     reported by <see cref="IOwnerResolver{TEntity}" />.
/// </summary>
/// <typeparam name="TEntity">The entity type being queried.</typeparam>
/// <remarks>
///     <para>Only activates when <typeparamref name="TEntity" /> implements <see cref="IOwnable" />.</para>
///     <para>Suppressed when <see cref="QueryOwnerSuppressed" /> is present in the advice context.</para>
///     <para>No filter is applied when the resolver returns <see langword="null" /> or empty.</para>
/// </remarks>
public sealed class AdviceBuildQueryOwner<TEntity>(IOwnerResolver<TEntity> resolver) : IRepositoryBuildQueryAdvisor<TEntity>
    where TEntity : class
{
    #region IRepositoryBuildQueryAdvisor<TEntity> Members

    /// <inheritdoc />
    public int Order => AdviceBuildQueryOwner.DefaultOrder;

    /// <inheritdoc />
    public async Task<AdviseResult> AdviseAsync(
        AdviceContext           ctx,
        QueryContainer<TEntity> container,
        CancellationToken       ct = default
    ) {
        if (ctx.Has<QueryOwnerSuppressed>()) {
            return AdviseResult.Continue;
        }

        if (!typeof(IOwnable).IsAssignableFrom(typeof(TEntity))) {
            return AdviseResult.Continue;
        }

        var owner = await resolver.ResolveAsync(ct);
        if (string.IsNullOrEmpty(owner)) {
            return AdviseResult.Continue;
        }

        container.ApplyModification(q => {
            return q.OfType<IOwnable>().Where(e => e.Owner == owner).OfType<TEntity>();
        });

        return AdviseResult.Continue;
    }

    #endregion
}
