using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Security.Skeleton;

namespace Schemata.Security.Foundation.Providers;

public class DefaultAccessProvider<T, TContext> : IAccessProvider<T, TContext>
{
    #region IAccessProvider<T,TContext> Members

    public Task<bool> HasAccessAsync(
        T?                entity,
        TContext?         context,
        ClaimsPrincipal?  principal,
        CancellationToken ct = default) {
        return Task.FromResult(true);
    }

    #endregion
}
