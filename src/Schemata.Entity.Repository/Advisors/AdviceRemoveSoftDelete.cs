using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;

namespace Schemata.Entity.Repository.Advisors;

/// <summary>Order constants for <see cref="AdviceRemoveSoftDelete{TEntity}"/>.</summary>
public static class AdviceRemoveSoftDelete
{
    /// <summary>Default execution order.</summary>
    public const int DefaultOrder = SchemataConstants.Orders.Max;
}

/// <summary>
///     Converts a physical delete into a soft-delete by setting <see cref="ISoftDelete.DeleteTime" /> and updating the entity instead.
/// </summary>
/// <typeparam name="TEntity">The entity type being removed.</typeparam>
/// <remarks>
///     <para>Order: <see cref="SchemataConstants.Orders.Max" />. Runs last in the remove pipeline.</para>
///     <para>Auto-registered by <see cref="Microsoft.Extensions.DependencyInjection.ServiceCollectionExtensions.AddRepository" />. Only activates for entities implementing <see cref="ISoftDelete" />.</para>
///     <para>Returns <see cref="AdviseResult.Handle" /> to prevent the physical delete from occurring.</para>
///     <para>Suppressed when <see cref="SuppressSoftDelete" /> is present in the advice context.</para>
/// </remarks>
public sealed class AdviceRemoveSoftDelete<TEntity> : IRepositoryRemoveAdvisor<TEntity>
    where TEntity : class
{
    #region IRepositoryRemoveAdvisor<TEntity> Members

    /// <inheritdoc />
    public int Order => AdviceRemoveSoftDelete.DefaultOrder;

    /// <inheritdoc />
    public async Task<AdviseResult> AdviseAsync(
        AdviceContext        ctx,
        IRepository<TEntity> repository,
        TEntity              entity,
        CancellationToken    ct
    ) {
        if (ctx.Has<SuppressSoftDelete>()) {
            return AdviseResult.Continue;
        }

        if (entity is not ISoftDelete trash) {
            return AdviseResult.Continue;
        }

        trash.DeleteTime = DateTime.UtcNow;

        await repository.UpdateAsync(entity, ct);

        return AdviseResult.Handle;
    }

    #endregion
}
