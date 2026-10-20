using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;

namespace Schemata.Tenancy.Foundation.Repository;

public sealed class AdviceUpdateTenant<TEntity> : IRepositoryUpdateAdvisor<TEntity>
    where TEntity : class
{
    // Runs after the generic update validation stage and before any uniqueness stage; the tenant
    // binding is verified once the entity itself has been validated.
    public int Order => AdviceUpdateValidation.DefaultOrder + 10_000_000;

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
