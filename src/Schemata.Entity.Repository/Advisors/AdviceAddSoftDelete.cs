using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;

namespace Schemata.Entity.Repository.Advisors;

/// <summary>Order constants for <see cref="AdviceAddSoftDelete{TEntity}"/>.</summary>
public static class AdviceAddSoftDelete
{
    /// <summary>Default execution order.</summary>
    public const int DefaultOrder = SchemataConstants.Orders.Max;
}

/// <summary>
///     Clears <see cref="ISoftDelete.DeleteTime" /> to <see langword="null" /> when a soft-deletable entity is added, ensuring it is not marked as deleted.
/// </summary>
/// <typeparam name="TEntity">The entity type being added.</typeparam>
/// <remarks>
///     <para>Order: <see cref="SchemataConstants.Orders.Max" />. Runs last in the add pipeline (after validation).</para>
///     <para>Auto-registered by <see cref="Microsoft.Extensions.DependencyInjection.ServiceCollectionExtensions.AddRepository" />. Only activates for entities implementing <see cref="ISoftDelete" />.</para>
///     <para>Suppressed when <see cref="SuppressSoftDelete" /> is present in the advice context.</para>
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
        if (ctx.Has<SuppressSoftDelete>()) {
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
