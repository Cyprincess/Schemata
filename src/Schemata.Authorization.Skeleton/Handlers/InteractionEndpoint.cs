using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Authorization.Skeleton.Models;

namespace Schemata.Authorization.Skeleton.Handlers;

/// <summary>
///     Abstract handler for the interaction endpoint.
///     Delegates to registered <see cref="IInteractionHandler" /> implementations based on
///     the <c>code_type</c> parameter.
/// </summary>
public abstract class InteractionEndpoint
{
    /// <summary>Returns details about a pending interaction identified by an opaque code.</summary>
    public abstract Task<AuthorizationResult> GetDetailsAsync(
        InteractRequest   request,
        string            issuer,
        CancellationToken ct
    );

    /// <summary>Approves a pending interaction on behalf of the authenticated user.</summary>
    public abstract Task<AuthorizationResult> ApproveAsync(
        InteractRequest   request,
        ClaimsPrincipal   principal,
        string            issuer,
        CancellationToken ct
    );

    /// <summary>Denies a pending interaction.</summary>
    public abstract Task DenyAsync(InteractRequest request, CancellationToken ct);
}
