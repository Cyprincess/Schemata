using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Entities;

namespace Schemata.Entity.Repository.Advices;

public class AdviceUpdateTimestamp<TEntity> : IRepositoryUpdateAsyncAdvice<TEntity>
{
    #region IRepositoryUpdateAsyncAdvice<TEntity> Members

    public int Order => 100_000_000;

    public int Priority => Order;

    public Task<bool> AdviseAsync(TEntity entity, CancellationToken ct) {
        if (entity is not ITimestamp time) return Task.FromResult(true);

        time.ModificationDate = DateTime.UtcNow;

        return Task.FromResult(true);
    }

    #endregion
}
