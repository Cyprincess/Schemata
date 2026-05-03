using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Authorization.Skeleton.Models;

namespace Schemata.Authorization.Skeleton.Handlers;

/// <summary>
///     Manages user-facing interaction flows (consent, device verification, logout).
///     Each implementation is identified by its <see cref="CodeType" /> URI.
/// </summary>
public interface IInteractionHandler
{
    /// <summary>URI identifying the interaction type, e.g. device verification or consent flow.</summary>
    string CodeType { get; }

    /// <summary>Returns details about a pending interaction.</summary>
    Task<AuthorizationResult> GetDetailsAsync(InteractRequest request, string issuer, CancellationToken ct);

    /// <summary>Approves a pending interaction on behalf of the authenticated user.</summary>
    Task<AuthorizationResult> ApproveAsync(
        InteractRequest   request,
        ClaimsPrincipal   principal,
        string            issuer,
        CancellationToken ct
    );

    /// <summary>Denies a pending interaction.</summary>
    Task DenyAsync(InteractRequest request, CancellationToken ct);
}
