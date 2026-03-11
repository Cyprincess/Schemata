using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;

namespace Schemata.Entity.Repository.Advisors;

public sealed class AdviceAddTimestamp<TEntity> : IRepositoryAddAdvisor<TEntity>
    where TEntity : class
{
    #region IRepositoryAddAdvisor<TEntity> Members

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

        if (entity is not ITimestamp timestamp) {
            return Task.FromResult(AdviseResult.Continue);
        }

        timestamp.CreateTime = DateTime.UtcNow;
        timestamp.UpdateTime = DateTime.UtcNow;

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
