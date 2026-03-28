using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Common;

namespace Schemata.Entity.Repository.Advisors;

/// <summary>Order constants for <see cref="AdviceAddCanonicalName{TEntity}" />.</summary>
public static class AdviceAddCanonicalName
{
    /// <summary>Default execution order.</summary>
    public const int DefaultOrder = AdviceAddConcurrency.DefaultOrder + 10_000_000;
}

/// <summary>
///     Resolves and sets <see cref="ICanonicalName.CanonicalName" /> from the entity's resource-name pattern when an
///     entity is added.
/// </summary>
/// <typeparam name="TEntity">The entity type being added.</typeparam>
/// <remarks>
///     <para>Order: 300,000,000.</para>
///     <para>
///         Auto-registered by
///         <see cref="Microsoft.Extensions.DependencyInjection.ServiceCollectionExtensions.AddRepository" />. Only
///         activates for entities implementing <see cref="ICanonicalName" />.
///     </para>
/// </remarks>
public sealed class AdviceAddCanonicalName<TEntity> : IRepositoryAddAdvisor<TEntity>
    where TEntity : class
{
    #region IRepositoryAddAdvisor<TEntity> Members

    /// <inheritdoc />
    public int Order => AdviceAddCanonicalName.DefaultOrder;

    /// <inheritdoc />
    public Task<AdviseResult> AdviseAsync(
        AdviceContext        ctx,
        IRepository<TEntity> repository,
        TEntity              entity,
        CancellationToken    ct
    ) {
        if (entity is not ICanonicalName named) {
            return Task.FromResult(AdviseResult.Continue);
        }

        var descriptor = ResourceNameDescriptor.ForType(entity.GetType());
        if (descriptor.Pattern is null) {
            return Task.FromResult(AdviseResult.Continue);
        }

        named.CanonicalName = descriptor.Resolve(entity);

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
