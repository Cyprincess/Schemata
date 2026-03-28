using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Authorization.Skeleton.Models;

namespace Schemata.Authorization.Skeleton.Handlers;

public abstract class InteractionEndpoint
{
    public abstract Task<AuthorizationResult> GetDetailsAsync(
        InteractRequest   request,
        string            issuer,
        CancellationToken ct
    );

    public abstract Task<AuthorizationResult> ApproveAsync(
        InteractRequest   request,
        ClaimsPrincipal   principal,
        string            issuer,
        CancellationToken ct
    );

    public abstract Task DenyAsync(InteractRequest request, CancellationToken ct);
}
