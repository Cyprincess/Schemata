using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Entities;

namespace Schemata.Entity.Repository.Advices;

public class AdviceRemoveConcurrency<TEntity> : IRepositoryRemoveAsyncAdvice<TEntity>
    where TEntity : class
{
    #region IRepositoryRemoveAsyncAdvice<TEntity> Members

    public int Order => 200_000_000;

    public int Priority => Order;

    public Task<bool> AdviseAsync(IRepository<TEntity> repository, TEntity entity, CancellationToken ct) {
        if (entity is not ITrash) {
            return Task.FromResult(true);
        }

        if (entity is not IConcurrency concurrency) {
            return Task.FromResult(true);
        }

        concurrency.Timestamp = Guid.NewGuid();

        return Task.FromResult(true);
    }

    #endregion
}
