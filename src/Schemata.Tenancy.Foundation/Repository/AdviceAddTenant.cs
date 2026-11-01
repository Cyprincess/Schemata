using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;

namespace Schemata.Tenancy.Foundation.Repository;

public static class AdviceAddTenant
{
    public const int DefaultOrder = AdviceAddCanonicalName.DefaultOrder - 1_000_000;
}

public sealed class AdviceAddTenant<TEntity> : IRepositoryAddAdvisor<TEntity>
    where TEntity : class
{
    public int Order => AdviceAddTenant.DefaultOrder;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext ctx,
        IRepository<TEntity> repository,
        TEntity entity,
        MutationOptions      options,
        CancellationToken ct = default
    ) {
        if (entity is ITenant tenant) {
            tenant.TenantUid = ctx.ServiceProvider.GetRequiredService<TenantRepositoryBinding>().Uid;
        }

        return Task.FromResult(AdviseResult.Continue);
    }
}
