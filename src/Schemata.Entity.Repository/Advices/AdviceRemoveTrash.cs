using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Entities;

namespace Schemata.Entity.Repository.Advices;

public sealed class AdviceRemoveTrash<TEntity> : IRepositoryRemoveAsyncAdvice<TEntity>
    where TEntity : class
{
    #region IRepositoryRemoveAsyncAdvice<TEntity> Members

    public int Order => int.MaxValue;

    public int Priority => Order;

    public async Task<bool> AdviseAsync(IRepository<TEntity> repository, TEntity entity, CancellationToken ct) {
        if (entity is not ITrash trash) {
            return true;
        }

        trash.DeletionDate = DateTime.UtcNow;

        await repository.UpdateAsync(entity, ct);

        return false;
    }

    #endregion
}
