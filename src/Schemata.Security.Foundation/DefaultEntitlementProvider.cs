using System;
using System.Linq.Expressions;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Security.Skeleton;

namespace Schemata.Security.Foundation;

/// <summary>Provides the default entitlement expression that leaves repository queries unfiltered.</summary>
public sealed class DefaultEntitlementProvider<T, TRequest> : IEntitlementProvider<T, TRequest>
{
    #region IEntitlementProvider<T,TRequest> Members

    public Task<Expression<Func<T, bool>>?> GenerateEntitlementExpressionAsync(
        AccessContext<TRequest> context,
        ClaimsPrincipal?        principal,
        CancellationToken       ct = default
    ) {
        return Task.FromResult<Expression<Func<T, bool>>?>(null);
    }

    #endregion
}
