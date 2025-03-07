using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advices;
using Schemata.Abstractions.Entities;

namespace Schemata.Entity.Repository.Advices;

public sealed class AdviceAddConcurrency<TEntity> : IRepositoryAddAsyncAdvice<TEntity> where TEntity : class
{
    #region IRepositoryAddAsyncAdvice<TEntity> Members

    public int Order => 200_000_000;

    public int Priority => Order;

    public Task<bool> AdviseAsync(
        AdviceContext        ctx,
        IRepository<TEntity> repository,
        TEntity              entity,
        CancellationToken    ct) {
        if (entity is not IConcurrency concurrency) {
            return Task.FromResult(true);
        }

        concurrency.Timestamp = Guid.NewGuid();

        return Task.FromResult(true);
    }

    #endregion
}
