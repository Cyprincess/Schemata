using System;
using System.Linq.Expressions;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Security.Skeleton;

namespace Schemata.Security.Foundation.Providers;

public sealed class DefaultEntitlementProvider<T, TContext> : IEntitlementProvider<T, TContext>
{
    #region IEntitlementProvider<T,TContext> Members

    public Task<Expression<Func<T, bool>>?> GenerateEntitlementExpressionAsync(
        TContext?         context,
        ClaimsPrincipal?  principal,
        CancellationToken ct = default) {
        return Task.FromResult<Expression<Func<T, bool>>?>(_ => true);
    }

    #endregion
}
