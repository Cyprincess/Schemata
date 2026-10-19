using System;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.Repository;

namespace Schemata.Tenancy.Foundation.Repository;

internal static class TenantMutation<TEntity>
    where TEntity : class
{
    private static readonly Lazy<Expression<Func<TEntity, Guid?>>> Projection = new(CreateProjection);

    internal static async Task ValidateAsync(IRepository<TEntity> repository, TEntity entity, Guid? tenant, CancellationToken ct) {
        var tenanted = (ITenant)entity;

        var original = await repository.GetOriginalAsync(entity, Projection.Value, ct, new QueryOptions(IgnoreSoftDelete: true));
        if (!original.Found) {
            throw new PermissionDeniedException();
        }
        if (tenanted.TenantUid != tenant || original.Value != tenant) {
            tenanted.TenantUid = original.Value;
            throw new PermissionDeniedException();
        }
    }

    private static Expression<Func<TEntity, Guid?>> CreateProjection() {
        var entity = Expression.Parameter(typeof(TEntity), "entity");
        return Expression.Lambda<Func<TEntity, Guid?>>(Expression.Property(entity, nameof(ITenant.TenantUid)), entity);
    }
}
