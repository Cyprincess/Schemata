using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;

namespace Schemata.Entity.Repository.Advisors;

/// <summary>Order constants for <see cref="AdviceAddConcurrency{TEntity}" />.</summary>
public static class AdviceAddConcurrency
{
    /// <summary>Default execution order.</summary>
    public const int DefaultOrder = AdviceAddTimestamp.DefaultOrder + 10_000_000;
}

/// <summary>
///     Generates a new concurrency stamp (<see cref="IConcurrency.Timestamp" />) when an entity is added.
/// </summary>
/// <typeparam name="TEntity">The entity type being added.</typeparam>
/// <remarks>
///     <para>Order: 200,000,000.</para>
///     <para>
///         Auto-registered by
///         <see cref="Microsoft.Extensions.DependencyInjection.ServiceCollectionExtensions.AddRepository" />. Only
///         activates for entities implementing <see cref="IConcurrency" />.
///     </para>
///     <para>Suppressed when <see cref="ConcurrencySuppressed" /> is present in the advice context.</para>
/// </remarks>
public sealed class AdviceAddConcurrency<TEntity> : IRepositoryAddAdvisor<TEntity>
    where TEntity : class
{
    #region IRepositoryAddAdvisor<TEntity> Members

    /// <inheritdoc />
    public int Order => AdviceAddConcurrency.DefaultOrder;

    /// <inheritdoc />
    public Task<AdviseResult> AdviseAsync(
        AdviceContext        ctx,
        IRepository<TEntity> repository,
        TEntity              entity,
        CancellationToken    ct
    ) {
        if (ctx.Has<ConcurrencySuppressed>()) {
            return Task.FromResult(AdviseResult.Continue);
        }

        if (entity is not IConcurrency concurrency) {
            return Task.FromResult(AdviseResult.Continue);
        }

        concurrency.Timestamp = Guid.NewGuid();

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
