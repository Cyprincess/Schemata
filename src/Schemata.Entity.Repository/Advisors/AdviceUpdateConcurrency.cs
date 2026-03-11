using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;

namespace Schemata.Entity.Repository.Advisors;

public sealed class AdviceUpdateConcurrency<TEntity> : IRepositoryUpdateAdvisor<TEntity>
    where TEntity : class
{
    #region IRepositoryUpdateAdvisor<TEntity> Members

    public int Order => int.MaxValue;

    public int Priority => Order;

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
