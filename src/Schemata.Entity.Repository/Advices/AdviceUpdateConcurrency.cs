using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Entities;

namespace Schemata.Entity.Repository.Advices;

public class AdviceUpdateConcurrency<TEntity> : IRepositoryUpdateAsyncAdvice<TEntity>
    where TEntity : class
{
    #region IRepositoryUpdateAsyncAdvice<TEntity> Members

    public int Order => int.MaxValue;

    public int Priority => Order;

    public async Task<bool> AdviseAsync(IRepository<TEntity> repository, TEntity entity, CancellationToken ct) {
        if (entity is not IConcurrency concurrency) return true;

        var stored = await repository.GetAsync(entity, ct);

        if (OfType<IConcurrency>(stored)?.Timestamp != concurrency.Timestamp) {
            throw new ConcurrencyException();
        }

        concurrency.Timestamp = Guid.NewGuid();

        return true;
    }

    #endregion

    public TResult? OfType<TResult>(object? entity)
        where TResult : class {
        return entity as TResult;
    }
}
