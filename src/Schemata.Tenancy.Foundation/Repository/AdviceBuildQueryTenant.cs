using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;

namespace Schemata.Tenancy.Foundation.Repository;

public sealed class AdviceBuildQueryTenant<TEntity> : IRepositoryBuildQueryAdvisor<TEntity>
    where TEntity : class
{
    public int Order => int.MinValue;

    public Task<AdviseResult> AdviseAsync(
        AdviceContext ctx,
        QueryContainer<TEntity> container,
        CancellationToken ct = default
    ) {
        if (!typeof(ITenant).IsAssignableFrom(typeof(TEntity))) {
            return Task.FromResult(AdviseResult.Continue);
        }

        var binding   = ctx.ServiceProvider.GetRequiredService<TenantRepositoryBinding>();
        var predicate = CreatePredicate(binding.Uid);
        container.ApplyModification(query => query.Where(predicate));
        return Task.FromResult(AdviseResult.Continue);
    }

    private static Expression<Func<TEntity, bool>> CreatePredicate(Guid? uid) {
        var entity = Expression.Parameter(typeof(TEntity), "entity");
        var tenant = Expression.Property(entity, nameof(ITenant.TenantUid));
        var value  = Expression.Constant(uid, typeof(Guid?));

        return Expression.Lambda<Func<TEntity, bool>>(Expression.Equal(tenant, value), entity);
    }
}
