using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;

namespace Schemata.Entity.Repository.Advisors;

public sealed class AdviceRemoveSoftDelete<TEntity> : IRepositoryRemoveAdvisor<TEntity>
    where TEntity : class
{
    #region IRepositoryRemoveAdvisor<TEntity> Members

    public int Order => int.MaxValue;

    public int Priority => Order;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext        ctx,
        IRepository<TEntity> repository,
        TEntity              entity,
        CancellationToken    ct
    ) {
        if (ctx.Has<SuppressSoftDelete>()) {
            return AdviseResult.Continue;
        }

        if (entity is not ISoftDelete trash) {
            return AdviseResult.Continue;
        }

        trash.DeleteTime = DateTime.UtcNow;

        await repository.UpdateAsync(entity, ct);

        return AdviseResult.Handle;
    }

    #endregion
}
