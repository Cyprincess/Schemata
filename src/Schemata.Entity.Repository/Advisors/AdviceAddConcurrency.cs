using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;

namespace Schemata.Entity.Repository.Advisors;

public sealed class AdviceAddConcurrency<TEntity> : IRepositoryAddAdvisor<TEntity>
    where TEntity : class
{
    #region IRepositoryAddAdvisor<TEntity> Members

    public int Order => 200_000_000;

    public int Priority => Order;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext        ctx,
        IRepository<TEntity> repository,
        TEntity              entity,
        CancellationToken    ct
    ) {
        if (ctx.Has<SuppressConcurrency>()) {
            return Task.FromResult(AdviseResult.Continue);
        }

        if (entity is not IConcurrency concurrency) {
            return Task.FromResult(AdviseResult.Continue);
        }

        concurrency.Timestamp = Guid.NewGuid();

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
