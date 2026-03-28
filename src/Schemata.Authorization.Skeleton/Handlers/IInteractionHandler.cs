using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Authorization.Skeleton.Models;

namespace Schemata.Authorization.Skeleton.Handlers;

/// <summary>Manages user-facing interaction flows (consent, device verification, logout) identified by a code type URI.</summary>
public interface IInteractionHandler
{
    string CodeType { get; }

    Task<AuthorizationResult> GetDetailsAsync(InteractRequest request, string issuer, CancellationToken ct);

    Task<AuthorizationResult> ApproveAsync(
        InteractRequest   request,
        ClaimsPrincipal   principal,
        string            issuer,
        CancellationToken ct
    );

    Task DenyAsync(InteractRequest request, CancellationToken ct);
}
