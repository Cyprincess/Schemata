using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Entity.Repository.Advisors;

/// <summary>Order constants for <see cref="AdviceUpdateTimestamp{TEntity}" />.</summary>
public static class AdviceUpdateTimestamp
{
    /// <summary>Default execution order.</summary>
    public const int DefaultOrder = Orders.Base;
}

/// <summary>
///     Sets <see cref="ITimestamp.UpdateTime" /> to <see cref="DateTime.UtcNow" /> when an entity is updated.
/// </summary>
/// <typeparam name="TEntity">The entity type being updated.</typeparam>
/// <remarks>
///     <para>Order: 100,000,000. Runs early in the update pipeline.</para>
///     <para>
///         Auto-registered by
///         <see cref="Microsoft.Extensions.DependencyInjection.ServiceCollectionExtensions.AddRepository" />. Only
///         activates for entities implementing <see cref="ITimestamp" />.
///     </para>
///     <para>Suppressed when <see cref="TimestampSuppressed" /> is present in the advice context.</para>
/// </remarks>
public sealed class AdviceUpdateTimestamp<TEntity> : IRepositoryUpdateAdvisor<TEntity>
    where TEntity : class
{
    #region IRepositoryUpdateAdvisor<TEntity> Members

    /// <inheritdoc />
    public int Order => AdviceUpdateTimestamp.DefaultOrder;

    /// <inheritdoc />
    public Task<AdviseResult> AdviseAsync(
        AdviceContext        ctx,
        IRepository<TEntity> repository,
        TEntity              entity,
        CancellationToken    ct
    ) {
        if (ctx.Has<TimestampSuppressed>()) {
            return Task.FromResult(AdviseResult.Continue);
        }

        if (entity is not ITimestamp time) {
            return Task.FromResult(AdviseResult.Continue);
        }

        time.UpdateTime = DateTime.UtcNow;

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
