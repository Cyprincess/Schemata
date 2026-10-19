using System;
using System.Linq;
using System.Linq.Expressions;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Entities;
using Schemata.Common;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Security.Skeleton;

namespace Schemata.Flow.Foundation;

/// <summary>
///     Read entitlement over the host-selected process entity: process rows match on their own
///     canonical name, token and transition rows match on their owning process.
/// </summary>
/// <typeparam name="TEntity">The resource entity type under evaluation.</typeparam>
/// <typeparam name="TRequest">The transport request type.</typeparam>
/// <typeparam name="TProcess">The persisted process entity type registered by the host.</typeparam>
/// <typeparam name="TToken">The persisted token entity type registered by the host.</typeparam>
/// <typeparam name="TParticipant">The persisted participant entity type registered by the host.</typeparam>
public sealed class FlowReadPolicy<TEntity, TRequest, TProcess, TToken, TParticipant>(
    FlowAccessPolicy<TProcess, TToken, TParticipant> policy
)
    : IAccessProvider<TEntity, TRequest>, IEntitlementProvider<TEntity, TRequest>
    where TEntity : class, ICanonicalName
    where TProcess : SchemataProcess
    where TToken : SchemataProcessToken
    where TParticipant : SchemataProcessParticipant, new()
{
    public async Task<AccessDecision> HasAccessAsync(TEntity? entity, AccessContext<TRequest> context, ClaimsPrincipal? principal, CancellationToken ct = default) {
        if (principal is null || !principal.Identities.Any(identity => identity.IsAuthenticated)) return AccessDecision.Denied;
        if (context.Operation == nameof(Operations.List)) return AccessDecision.Allowed;
        var name = entity?.CanonicalName ?? context.Name;
        if (string.IsNullOrWhiteSpace(name)) return AccessDecision.Denied;
        var process = ProcessName(name);
        return await policy.CanReadAsync(process, typeof(TEntity), context.Operation ?? string.Empty, principal, ct) ? AccessDecision.Allowed : AccessDecision.Denied;
    }

    public async Task<Expression<Func<TEntity, bool>>?> GenerateEntitlementExpressionAsync(AccessContext<TRequest> context, ClaimsPrincipal? principal, CancellationToken ct = default) {
        var global = policy.HasPermission(context.Operation ?? string.Empty, typeof(TEntity), principal);
        var names = global ? [] : await policy.ReadableProcessesAsync(principal, ct);
        Expression expression;
        var row = Expression.Parameter(typeof(TEntity), "row");
        if (global) expression = Expression.Constant(true);
        else {
            var values = typeof(TEntity) == typeof(TProcess)
                ? names
                : names.Select(ProcessLeafName).ToArray();
            var property = typeof(TEntity) == typeof(TProcess) ? nameof(ICanonicalName.CanonicalName) : nameof(SchemataProcessToken.Process);
            expression = Expression.Call(typeof(Enumerable), nameof(Enumerable.Contains), [typeof(string)], Expression.Constant(values), Expression.Property(row, property));
        }
        return Expression.Lambda<Func<TEntity, bool>>(expression, row);
    }

    private static string ProcessName(string canonical) {
        return typeof(TEntity) == typeof(TProcess) ? canonical : ProcessCanonicalName(canonical);
    }

    private static string ProcessCanonicalName(string canonical) {
        var depth = ResourceNameDescriptor.ForType<TProcess>().CollectionPath.Split('/').Length + 1;
        var parts = canonical.Split('/');
        return parts.Length >= depth ? string.Join('/', parts.Take(depth)) : canonical;
    }

    private static string ProcessLeafName(string canonical) {
        return ResourceNameDescriptor.ForType<TProcess>().ParseCanonicalName(canonical)?.LeafName ?? canonical;
    }

}
