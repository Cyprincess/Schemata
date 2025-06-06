using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advices;
using Schemata.Abstractions.Entities;

namespace Schemata.Entity.Repository.Advices;

public sealed class AdviceAddTimestamp<TEntity> : IRepositoryAddAsyncAdvice<TEntity> where TEntity : class
{
    #region IRepositoryAddAsyncAdvice<TEntity> Members

    public int Order => 100_000_000;

    public int Priority => Order;

    public Task<bool> AdviseAsync(
        AdviceContext        ctx,
        IRepository<TEntity> repository,
        TEntity              entity,
        CancellationToken    ct) {
        if (entity is not ITimestamp timestamp) {
            return Task.FromResult(true);
        }

        timestamp.CreateTime = DateTime.UtcNow;
        timestamp.UpdateTime = DateTime.UtcNow;

        return Task.FromResult(true);
    }

    #endregion
}
