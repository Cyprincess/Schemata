using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Common;

namespace Schemata.Entity.Repository.Advisors;

/// <summary>Order constants for <see cref="AdviceAddConcurrency{TEntity}" />.</summary>
public static class AdviceAddConcurrency
{
    /// <summary>
    ///     Default execution order: after <see cref="AdviceAddTimestamp{TEntity}" />
    ///     (100,000,000 + 10,000,000 = 110,000,000).
    /// </summary>
    public const int DefaultOrder = AdviceAddTimestamp.DefaultOrder + 10_000_000;
}

/// <summary>
///     Generates a new concurrency stamp (<see cref="IConcurrency.Timestamp" />) when an
///     entity is added, per
///     <seealso href="https://google.aip.dev/154">AIP-154: Resource freshness validation</seealso>.
/// </summary>
/// <typeparam name="TEntity">The entity type being added.</typeparam>
/// <remarks>
///     Only activates for entities implementing <see cref="IConcurrency" />.
/// </remarks>
public sealed class AdviceAddConcurrency<TEntity> : IRepositoryAddAdvisor<TEntity>
    where TEntity : class
{
    #region IRepositoryAddAdvisor<TEntity> Members

    public int Order => AdviceAddConcurrency.DefaultOrder;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext        ctx,
        IRepository<TEntity> repository,
        TEntity              entity,
        CancellationToken    ct
    ) {
        if (entity is not IConcurrency concurrency) {
            return Task.FromResult(AdviseResult.Continue);
        }

        concurrency.Timestamp = Identifiers.NewUid();

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
