using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;

namespace Schemata.Entity.Repository.Advisors;

public sealed class AdviceUpdateTimestamp<TEntity> : IRepositoryUpdateAdvisor<TEntity>
    where TEntity : class
{
    #region IRepositoryUpdateAdvisor<TEntity> Members

    public int Order => 100_000_000;

    public int Priority => Order;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext        ctx,
        IRepository<TEntity> repository,
        TEntity              entity,
        CancellationToken    ct
    ) {
        if (ctx.Has<SuppressTimestamp>()) {
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
