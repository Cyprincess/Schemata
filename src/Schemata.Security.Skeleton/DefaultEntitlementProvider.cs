using System;
using System.Linq.Expressions;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Security.Skeleton;

/// <summary>
///     Default entitlement provider that applies no data filtering, granting visibility to all entities.
/// </summary>
/// <typeparam name="T">The entity type.</typeparam>
/// <typeparam name="TContext">The context type.</typeparam>
/// <remarks>
///     Returns a predicate equivalent to <c>_ => true</c> so that all rows pass through.
///     Replace with a custom <see cref="IEntitlementProvider{T, TContext}"/> to enforce row-level security.
/// </remarks>
public sealed class DefaultEntitlementProvider<T, TContext> : IEntitlementProvider<T, TContext>
{
    #region IEntitlementProvider<T,TContext> Members

    /// <inheritdoc />
    public Task<Expression<Func<T, bool>>?> GenerateEntitlementExpressionAsync(
        TContext?         context,
        ClaimsPrincipal?  principal,
        CancellationToken ct = default
    ) {
        return Task.FromResult<Expression<Func<T, bool>>?>(_ => true);
    }

    #endregion
}
