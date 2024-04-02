using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Entities;

namespace Schemata.Entity.Repository.Advices;

public class AdviceQueryTrash<TEntity> : IRepositoryQueryAsyncAdvice<TEntity>
    where TEntity : class
{
    #region IRepositoryQueryAsyncAdvice<TEntity> Members

    public int Order => 100_000_000;

    public int Priority => Order;

    public Task<bool> AdviseAsync(
        IRepository<TEntity>    repository,
        QueryContainer<TEntity> query,
        CancellationToken       ct = default) {
        if (!typeof(ITrash).IsAssignableFrom(typeof(TEntity))) return Task.FromResult(true);

        query.ApplyModification(q => {
            return q.OfType<ITrash>().Where(e => e.DeletionDate == null).OfType<TEntity>();
        });

        return Task.FromResult(true);
    }

    #endregion
}
