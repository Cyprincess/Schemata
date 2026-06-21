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
/// <typeparam name="T">Entity type filtered by the expression.</typeparam>
/// <typeparam name="TRequest">Request payload type used by the operation.</typeparam>
public interface IEntitlementProvider<T, TRequest>
{
    /// <summary>Returns a filter expression for repository queries.</summary>
    /// <param name="context">Operation and request details.</param>
    /// <param name="principal">Principal requesting access.</param>
    /// <param name="ct">A cancellation token.</param>
    Task<Expression<Func<T, bool>>?> GenerateEntitlementExpressionAsync(
        AccessContext<TRequest> context,
        ClaimsPrincipal?        principal,
        CancellationToken       ct = default
    );
}
