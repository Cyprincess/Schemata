using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Common;

namespace Schemata.Entity.Repository.Advisors;

public sealed class AdviceAddCanonicalName<TEntity> : IRepositoryAddAdvisor<TEntity>
    where TEntity : class
{
    #region IRepositoryAddAdvisor<TEntity> Members

    public int Order => 300_000_000;

    public int Priority => Order;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext        ctx,
        IRepository<TEntity> repository,
        TEntity              entity,
        CancellationToken    ct
    ) {
        if (entity is not ICanonicalName named) {
            return Task.FromResult(AdviseResult.Continue);
        }

        var descriptor = ResourceNameDescriptor.ForType(entity.GetType());
        if (descriptor.Pattern is null) {
            return Task.FromResult(AdviseResult.Continue);
        }

        named.CanonicalName = descriptor.Resolve(entity);

        return Task.FromResult(AdviseResult.Continue);
    }

    #endregion
}
