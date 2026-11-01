using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;

namespace Schemata.Tenancy.Foundation.Repository;

public sealed class AdviceRemoveTenant<TEntity> : IRepositoryRemoveAdvisor<TEntity>
    where TEntity : class
{
    public int Order => int.MinValue;

    public async Task<AdviseResult> AdviseAsync(
        AdviceContext ctx,
        IRepository<TEntity> repository,
        TEntity entity,
        MutationOptions      options,
        CancellationToken ct = default
    ) {
        if (entity is ITenant) {
            var binding = ctx.ServiceProvider.GetRequiredService<TenantRepositoryBinding>();
            await TenantMutation<TEntity>.ValidateAsync(repository, entity, binding.Uid, ct);
        }

        return AdviseResult.Continue;
    }
}
