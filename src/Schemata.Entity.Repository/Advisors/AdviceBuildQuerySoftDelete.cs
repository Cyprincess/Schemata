using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;

namespace Schemata.Entity.Repository.Advisors;

public sealed class AdviceBuildQuerySoftDelete<TEntity> : IRepositoryBuildQueryAdvisor<TEntity>
    where TEntity : class
{
    #region IRepositoryBuildQueryAdvisor<TEntity> Members

    public int Order => 100_000_000;

    public int Priority => Order;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext           ctx,
        QueryContainer<TEntity> container,
        CancellationToken       ct = default
    ) {
        if (ctx.Has<SuppressQuerySoftDelete>()) {
            return Task.FromResult(AdviseResult.Continue);
        }

        if (!typeof(ISoftDelete).IsAssignableFrom(typeof(TEntity))) {
            return Task.FromResult(AdviseResult.Continue);
        }

        container.ApplyModification(q => {
            return q.OfType<ISoftDelete>().Where(e => e.DeleteTime == null).OfType<TEntity>();
        });

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
