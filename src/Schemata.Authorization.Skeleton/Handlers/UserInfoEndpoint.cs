using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Authorization.Skeleton.Handlers;

public abstract class UserInfoEndpoint
{
    public abstract Task<AuthorizationResult> HandleAsync(ClaimsPrincipal principal, CancellationToken ct);
}
