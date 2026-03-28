using System;
using System.Linq.Expressions;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Security.Skeleton;

/// <summary>
///     Generates LINQ filter expressions for row-level security, composed into repository queries
///     so unauthorized entities are excluded at the data layer.
/// </summary>
public interface IEntitlementProvider<T, TRequest>
{
    /// <summary>Returns a filter expression, or null for no additional filtering.</summary>
    Task<Expression<Func<T, bool>>?> GenerateEntitlementExpressionAsync(
        AccessContext<TRequest> context,
        ClaimsPrincipal?        principal,
        CancellationToken       ct = default
    );
}
