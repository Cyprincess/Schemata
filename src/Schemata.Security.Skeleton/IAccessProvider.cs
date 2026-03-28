using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Security.Skeleton;

public interface IAccessProvider<T, TRequest>
{
    Task<bool> HasAccessAsync(
        T?                      entity,
        AccessContext<TRequest> context,
        ClaimsPrincipal?        principal,
        CancellationToken       ct = default
    );
}
