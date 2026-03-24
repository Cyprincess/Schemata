using System;
using System.Linq.Expressions;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Security.Skeleton;

/// <summary>
///     Generates query-level filter expressions that restrict data visibility based on the current principal.
/// </summary>
/// <typeparam name="T">The entity type to filter.</typeparam>
/// <typeparam name="TContext">The context type that provides additional entitlement information.</typeparam>
/// <remarks>
///     Implementations provide row-level security by producing LINQ expressions that are composed into
///     repository queries. This ensures unauthorized entities are excluded at the data layer rather than
///     being filtered after retrieval.
/// </remarks>
public interface IEntitlementProvider<T, TContext>
{
    /// <summary>
    ///     Produces a predicate expression that filters entities the principal is entitled to see.
    /// </summary>
    /// <param name="context">The context providing additional entitlement state.</param>
    /// <param name="principal">The claims principal representing the current user.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>
    ///     A filter expression to apply to queries, or <see langword="null"/> to apply no additional filtering.
    /// </returns>
    Task<Expression<Func<T, bool>>?> GenerateEntitlementExpressionAsync(
        TContext?         context,
        ClaimsPrincipal?  principal,
        CancellationToken ct = default
    );
}
