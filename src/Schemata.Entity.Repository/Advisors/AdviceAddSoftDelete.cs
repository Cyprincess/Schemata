using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;

namespace Schemata.Entity.Repository.Advisors;

public sealed class AdviceAddSoftDelete<TEntity> : IRepositoryAddAdvisor<TEntity>
    where TEntity : class
{
    #region IRepositoryAddAdvisor<TEntity> Members

    public int Order => int.MaxValue;

    public int Priority => Order;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext        ctx,
        IRepository<TEntity> repository,
        TEntity              entity,
        CancellationToken    ct
    ) {
        if (ctx.Has<SuppressSoftDelete>()) {
            return Task.FromResult(AdviseResult.Continue);
        }

        if (entity is not ISoftDelete trash) {
            return Task.FromResult(AdviseResult.Continue);
        }

        trash.DeleteTime = null;

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
