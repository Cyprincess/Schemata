using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;

namespace Schemata.Entity.Repository.Advisors;

/// <summary>Order constants for <see cref="AdviceAddTimestamp{TEntity}"/>.</summary>
public static class AdviceAddTimestamp
{
    /// <summary>Default execution order.</summary>
    public const int DefaultOrder = SchemataConstants.Orders.Base;
}

/// <summary>
///     Sets <see cref="ITimestamp.CreateTime" /> and <see cref="ITimestamp.UpdateTime" /> to <see cref="DateTime.UtcNow" /> when an entity is added.
/// </summary>
/// <typeparam name="TEntity">The entity type being added.</typeparam>
/// <remarks>
///     <para>Order: 100,000,000. Runs early in the add pipeline.</para>
///     <para>Auto-registered by <see cref="Microsoft.Extensions.DependencyInjection.ServiceCollectionExtensions.AddRepository" />. Only activates for entities implementing <see cref="ITimestamp" />.</para>
///     <para>Suppressed when <see cref="SuppressTimestamp" /> is present in the advice context.</para>
/// </remarks>
public sealed class AdviceAddTimestamp<TEntity> : IRepositoryAddAdvisor<TEntity>
    where TEntity : class
{
    #region IRepositoryAddAdvisor<TEntity> Members

    /// <inheritdoc />
    public int Order => AdviceAddTimestamp.DefaultOrder;

    /// <inheritdoc />
    public Task<AdviseResult> AdviseAsync(
        AdviceContext        ctx,
        IRepository<TEntity> repository,
        TEntity              entity,
        CancellationToken    ct
    ) {
        if (ctx.Has<SuppressTimestamp>()) {
            return Task.FromResult(AdviseResult.Continue);
        }

        if (entity is not ITimestamp timestamp) {
            return Task.FromResult(AdviseResult.Continue);
        }

        timestamp.CreateTime = DateTime.UtcNow;
        timestamp.UpdateTime = DateTime.UtcNow;

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
