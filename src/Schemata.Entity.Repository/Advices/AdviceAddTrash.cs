using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Entity;

namespace Schemata.Entity.Repository.Advices;

public class AdviceAddTrash<TEntity> : IRepositoryAddAsyncAdvice<TEntity>
{
    #region IRepositoryAddAsyncAdvice<TEntity> Members

    public int Order => int.MaxValue;

    public int Priority => Order;

    public Task<bool> AdviseAsync(TEntity entity, CancellationToken ct) {
        if (entity is not ITrash trash) return Task.FromResult(true);

        trash.DeletionDate = null;

        return Task.FromResult(true);
    }

    #endregion
}
