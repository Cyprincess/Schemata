using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;

namespace Schemata.Entity.Repository.Advices;

public sealed class AdviceUpdateConcurrency<TEntity> : IRepositoryUpdateAsyncAdvice<TEntity>
    where TEntity : class
{
    #region IRepositoryUpdateAsyncAdvice<TEntity> Members

    public int Order => int.MaxValue;

    public int Priority => Order;

    public async Task<bool> AdviseAsync(IRepository<TEntity> repository, TEntity entity, CancellationToken ct) {
        if (entity is not IConcurrency concurrency) {
            return true;
        }

        var stored = await repository.GetAsync<IConcurrency>(entity, ct);

        if (stored?.Timestamp != concurrency.Timestamp) {
            throw new ConcurrencyException();
        }

        concurrency.Timestamp = Guid.NewGuid();

        return true;
    }

    #endregion
}
