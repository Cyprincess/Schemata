using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advices;
using Schemata.Abstractions.Entities;

namespace Schemata.Entity.Repository.Advices;

public sealed class AdviceBuildBuildQuerySoftDelete<TEntity> : IRepositoryBuildQueryAdvice<TEntity>
    where TEntity : class
{
    #region IRepositoryBuildQueryAdvice<TEntity> Members

    public int Order => 100_000_000;

    public int Priority => Order;

    public Task<bool> AdviseAsync(
        AdviceContext           ctx,
        QueryContainer<TEntity> container,
        CancellationToken       ct = default) {
        if (ctx.Has<SuppressQuerySoftDelete>()) {
            return Task.FromResult(true);
        }

        if (!typeof(ISoftDelete).IsAssignableFrom(typeof(TEntity))) {
            return Task.FromResult(true);
        }

        container.ApplyModification(q => {
            return q.OfType<ISoftDelete>().Where(e => e.DeleteTime == null).OfType<TEntity>();
        });

        return Task.FromResult(true);
    }

    #endregion
}
