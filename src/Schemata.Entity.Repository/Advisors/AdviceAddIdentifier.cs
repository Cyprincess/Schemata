using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Common;
using Schemata.Entity.Repository;

namespace Schemata.Entity.Repository.Advisors;

/// <summary>Order constants for <see cref="AdviceAddIdentifier{TEntity}" />.</summary>
public static class AdviceAddIdentifier
{
    /// <summary>Default execution order, immediately before <see cref="AdviceAddTimestamp{TEntity}" />.</summary>
    public const int DefaultOrder = AdviceAddTimestamp.DefaultOrder - 10_000_000;
}

/// <summary>
///     Assigns a fresh <see cref="Guid" /> to an added entity whose <see cref="IIdentifier.Uid" /> is empty.
///     Supplied identifiers remain unchanged.
/// </summary>
/// <typeparam name="TEntity">The entity type being added.</typeparam>
public sealed class AdviceAddIdentifier<TEntity> : IRepositoryAddAdvisor<TEntity>
    where TEntity : class
{
    public int Order => AdviceAddIdentifier.DefaultOrder;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext        ctx,
        IRepository<TEntity> repository,
        TEntity              entity,
        CancellationToken    ct
    ) {
        if (entity is IIdentifier { Uid: var uid } identified && uid == Guid.Empty) {
            identified.Uid = Identifiers.NewUid();
        }

        return Task.FromResult(AdviseResult.Continue);
    }
}