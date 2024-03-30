using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Entities;

namespace Schemata.Entity.Repository.Advices;

public class AdviceRemoveTrash<TEntity> : IRepositoryRemoveAsyncAdvice<TEntity>
{
    #region IRepositoryRemoveAsyncAdvice<TEntity> Members

    public int Order => int.MaxValue;

    public int Priority => Order;

    public Task<bool> AdviseAsync(TEntity entity, CancellationToken ct) {
        if (entity is not ITrash trash) return Task.FromResult(true);

        trash.DeletionDate = DateTime.UtcNow;

        return Task.FromResult(false);
    }

    #endregion
}
