using System;
using System.Linq.Expressions;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Security.Skeleton;

public interface IEntitlementProvider<T, TContext>
{
    Task<Expression<Func<T, bool>>?> GenerateEntitlementExpressionAsync(
        TContext?         context,
        ClaimsPrincipal?  principal,
        CancellationToken ct = default);
}
