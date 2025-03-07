using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advices;
using Schemata.Abstractions.Entities;

namespace Schemata.Entity.Repository.Advices;

public sealed class AdviceUpdateTimestamp<TEntity> : IRepositoryUpdateAsyncAdvice<TEntity> where TEntity : class
{
    #region IRepositoryUpdateAsyncAdvice<TEntity> Members

    public int Order => 100_000_000;

    public int Priority => Order;

    public Task<bool> AdviseAsync(
        AdviceContext        ctx,
        IRepository<TEntity> repository,
        TEntity              entity,
        CancellationToken    ct) {
        if (entity is not ITimestamp time) {
            return Task.FromResult(true);
        }

        time.UpdateTime = DateTime.UtcNow;

        return Task.FromResult(true);
    }

    #endregion
}
