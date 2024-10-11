using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Security.Skeleton;

public interface IAccessProvider<T, TContext>
{
    Task<bool> HasAccessAsync(
        T?                entity,
        TContext?         context,
        ClaimsPrincipal?  principal,
        CancellationToken ct = default);
}
