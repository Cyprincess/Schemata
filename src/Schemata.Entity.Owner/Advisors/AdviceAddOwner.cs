using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;

namespace Schemata.Entity.Owner.Advisors;

/// <summary>Order constants for <see cref="AdviceAddOwner{TEntity}" />.</summary>
public static class AdviceAddOwner
{
    /// <summary>Default execution order.</summary>
    public const int DefaultOrder = AdviceAddCanonicalName.DefaultOrder + 10_000_000;
}

/// <summary>
///     Populates <see cref="IOwnable.Owner" /> from <see cref="IOwnerResolver{TEntity}" /> when an entity is added.
/// </summary>
/// <typeparam name="TEntity">The entity type being added.</typeparam>
/// <remarks>
///     <para>Runs after <see cref="AdviceAddCanonicalName{TEntity}" /> so the entity's own canonical name is settled first.</para>
///     <para>Only activates when <typeparamref name="TEntity" /> implements <see cref="IOwnable" />.</para>
///     <para>Suppressed when <see cref="OwnerSuppressed" /> is present in the advice context.</para>
///     <para>Leaves an already-set <see cref="IOwnable.Owner" /> untouched so callers can override the default.</para>
/// </remarks>
public sealed class AdviceAddOwner<TEntity>(IOwnerResolver<TEntity> resolver) : IRepositoryAddAdvisor<TEntity>
    where TEntity : class
{
    #region IRepositoryAddAdvisor<TEntity> Members

    public int Order => AdviceAddOwner.DefaultOrder;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext        ctx,
        IRepository<TEntity> repository,
        TEntity              entity,
        CancellationToken    ct
    ) {
        if (ctx.Has<OwnerSuppressed>()) {
            return AdviseResult.Continue;
        }

        if (entity is not IOwnable ownable) {
            return AdviseResult.Continue;
        }

        if (!string.IsNullOrEmpty(ownable.Owner)) {
            return AdviseResult.Continue;
        }

        ownable.Owner = await resolver.ResolveAsync(ct);

        return AdviseResult.Continue;
    }

    #endregion
}
