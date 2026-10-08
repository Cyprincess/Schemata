using System;
using System.Linq;
using System.Linq.Expressions;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Tenancy;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Security.Skeleton;

namespace Schemata.Flow.Foundation;

public sealed class FlowReadPolicy<TEntity, TRequest>(FlowAccessPolicy policy)
    : IAccessProvider<TEntity, TRequest>, IEntitlementProvider<TEntity, TRequest>
    where TEntity : class, ICanonicalName
{
    public async Task<AccessDecision> HasAccessAsync(TEntity? entity, AccessContext<TRequest> context, ClaimsPrincipal? principal, CancellationToken ct = default) {
        if (principal is null || !principal.Identities.Any(identity => identity.IsAuthenticated)) return AccessDecision.Denied;
        if (context.Operation == nameof(Operations.List)) return AccessDecision.Allowed;
        var name = entity?.CanonicalName ?? context.Name;
        if (string.IsNullOrWhiteSpace(name)) return AccessDecision.Denied;
        if (entity is not null && Tenant(entity) != TenantContext.Current.Uid) return AccessDecision.Denied;
        var process = ProcessName(name);
        return await policy.CanReadAsync(process, typeof(TEntity), context.Operation ?? string.Empty, principal, ct) ? AccessDecision.Allowed : AccessDecision.Denied;
    }

    public async Task<Expression<Func<TEntity, bool>>?> GenerateEntitlementExpressionAsync(AccessContext<TRequest> context, ClaimsPrincipal? principal, CancellationToken ct = default) {
        var tenant = TenantContext.Current.Uid;
        var global = policy.HasPermission(context.Operation ?? string.Empty, typeof(TEntity), principal);
        var names = global ? [] : await policy.ReadableProcessesAsync(principal, ct);
        Expression expression;
        var row = Expression.Parameter(typeof(TEntity), "row");
        var tenantMatch = Expression.Equal(Expression.Property(row, nameof(SchemataProcess.TenantUid)), Expression.Constant(tenant, typeof(Guid?)));
        if (global) expression = tenantMatch;
        else {
            var values = typeof(TEntity) == typeof(SchemataProcess) ? names : names.Select(name => name["processes/".Length..]).ToArray();
            var property = typeof(TEntity) == typeof(SchemataProcess) ? nameof(ICanonicalName.CanonicalName) : nameof(SchemataProcessToken.Process);
            expression = Expression.AndAlso(tenantMatch, Expression.Call(typeof(Enumerable), nameof(Enumerable.Contains), [typeof(string)], Expression.Constant(values), Expression.Property(row, property)));
        }
        return Expression.Lambda<Func<TEntity, bool>>(expression, row);
    }

    private static string ProcessName(string canonical) {
        var end = canonical.IndexOf('/', "processes/".Length);
        return end < 0 ? canonical : canonical[..end];
    }

    private static Guid? Tenant(TEntity entity) => entity switch {
        SchemataProcess process => process.TenantUid,
        SchemataProcessToken token => token.TenantUid,
        SchemataProcessTransition transition => transition.TenantUid,
        _ => throw new InvalidOperationException("Unsupported Flow read entity."),
    };
}
