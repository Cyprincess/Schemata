using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;

namespace Schemata.Entity.Repository.Advisors;

/// <summary>Order constants for <see cref="AdviceUpdateConcurrency{TEntity}"/>.</summary>
public static class AdviceUpdateConcurrency
{
    /// <summary>Default execution order.</summary>
    public const int DefaultOrder = SchemataConstants.Orders.Max;
}

/// <summary>
///     Verifies the concurrency stamp matches the stored value and generates a new stamp on update.
/// </summary>
/// <typeparam name="TEntity">The entity type being updated.</typeparam>
/// <remarks>
///     <para>Order: <see cref="SchemataConstants.Orders.Max" />. Runs last in the update pipeline.</para>
///     <para>Auto-registered by <see cref="Microsoft.Extensions.DependencyInjection.ServiceCollectionExtensions.AddRepository" />. Only activates for entities implementing <see cref="IConcurrency" />.</para>
///     <para>Throws <see cref="ConcurrencyException" /> when the stored stamp does not match the entity's stamp.</para>
///     <para>Suppressed when <see cref="SuppressConcurrency" /> is present in the advice context.</para>
/// </remarks>
public sealed class AdviceUpdateConcurrency<TEntity> : IRepositoryUpdateAdvisor<TEntity>
    where TEntity : class
{
    #region IRepositoryUpdateAdvisor<TEntity> Members

    /// <inheritdoc />
    public int Order => AdviceUpdateConcurrency.DefaultOrder;

    /// <inheritdoc />
    public async Task<AdviseResult> AdviseAsync(
        AdviceContext        ctx,
        IRepository<TEntity> repository,
        TEntity              entity,
        CancellationToken    ct
    ) {
        if (ctx.Has<SuppressConcurrency>()) {
            return AdviseResult.Continue;
        }

        if (entity is not IConcurrency concurrency) {
            return AdviseResult.Continue;
        }

        var stored = await repository.GetAsync<IConcurrency>(entity, ct);

        if (stored is null) {
            return AdviseResult.Continue;
        }

        if (stored.Timestamp != concurrency.Timestamp) {
            throw new ConcurrencyException();
        }

        concurrency.Timestamp = Guid.NewGuid();

        return AdviseResult.Continue;
    }

    #endregion
}
