using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Humanizer;
using Schemata.Security.Skeleton;

namespace Schemata.Resource.Foundation.Security;

public class ResourceAccessProvider<T, TRequest> : IAccessProvider<T, ResourceRequestContext<TRequest>>
{
    #region IAccessProvider<T,ResourceRequestContext<TRequest>> Members

    public Task<bool> HasAccessAsync(
        T?                                resource,
        ResourceRequestContext<TRequest>? context,
        ClaimsPrincipal?                  principal,
        CancellationToken                 ct = default) {
        const string role = "resource-{operation}-{entity}";

        var entity    = typeof(T).Name.Kebaberize();
        var operation = context?.Operation.Humanize().Kebaberize();

        if (principal is null) {
            return Task.FromResult(false);
        }

        if (principal.HasClaim(ClaimTypes.Role, role.Replace("{operation}", operation).Replace("{entity}", entity))) {
            return Task.FromResult(true);
        }

        if (principal.HasClaim(ClaimTypes.Role, role.Replace("{operation}", "*").Replace("{entity}", entity))) {
            return Task.FromResult(true);
        }

        if (principal.HasClaim(ClaimTypes.Role, role.Replace("{operation}", operation).Replace("{entity}", "*"))) {
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    #endregion
}
