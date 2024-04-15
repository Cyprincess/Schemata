using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advices;
using Schemata.Abstractions.Entities;

namespace Schemata.Entity.Repository.Advices;

public sealed class AdviceAddSoftDelete<TEntity> : IRepositoryAddAsyncAdvice<TEntity>
    where TEntity : class
{
    #region IRepositoryAddAsyncAdvice<TEntity> Members

    public int Order => int.MaxValue;

    public int Priority => Order;

    public Task<bool> AdviseAsync(
        AdviceContext        ctx,
        IRepository<TEntity> repository,
        TEntity              entity,
        CancellationToken    ct) {
        if (entity is not ISoftDelete trash) {
            return Task.FromResult(true);
        }

        trash.DeleteTime = null;

        return Task.FromResult(true);
    }

    #endregion
}
