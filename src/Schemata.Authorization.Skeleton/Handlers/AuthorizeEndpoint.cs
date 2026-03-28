using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Authorization.Skeleton.Models;

namespace Schemata.Authorization.Skeleton.Handlers;

public abstract class AuthorizeEndpoint
{
    public abstract Task<AuthorizationResult> AuthorizeAsync(
        AuthorizeRequest  request,
        ClaimsPrincipal   principal,
        CancellationToken ct
    );
}
