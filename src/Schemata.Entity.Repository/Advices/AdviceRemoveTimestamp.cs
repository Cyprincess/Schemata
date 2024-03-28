using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Entity;

namespace Schemata.Entity.Repository.Advices;

public class AdviceRemoveTimestamp<TEntity> : IRepositoryRemoveAsyncAdvice<TEntity>
{
    #region IRepositoryRemoveAsyncAdvice<TEntity> Members

    public int Order => 100_000_000;

    public int Priority => Order;

    public Task<bool> AdviseAsync(TEntity entity, CancellationToken ct) {
        if (entity is not ITrash) return Task.FromResult(true);
        if (entity is not ITimestamp time) return Task.FromResult(true);

        time.ModificationDate = DateTime.UtcNow;

        return Task.FromResult(true);
    }

    #endregion
}
