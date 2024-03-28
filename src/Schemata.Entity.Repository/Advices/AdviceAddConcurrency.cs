using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Entities;

namespace Schemata.Entity.Repository.Advices;

public class AdviceAddConcurrency<TEntity> : IRepositoryAddAsyncAdvice<TEntity>
{
    #region IRepositoryAddAsyncAdvice<TEntity> Members

    public int Order => 200_000_000;

    public int Priority => Order;

    public Task<bool> AdviseAsync(TEntity entity, CancellationToken ct) {
        if (entity is not IConcurrency concurrency) return Task.FromResult(true);

        concurrency.Timestamp = Guid.NewGuid();

        return Task.FromResult(true);
    }

    #endregion
}
