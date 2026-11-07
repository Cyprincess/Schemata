using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Authorization.Skeleton.Services;

/// <summary>Context in which a consumer selects resource authentication requirements.</summary>
/// <param name="Principal">Validated resource principal or current authorization-session principal.</param>
/// <param name="Resource">Selected resource indicator or accepted token audience.</param>
/// <param name="RequestedAcrValues">Authentication classes already requested by the client.</param>
/// <param name="MaxAge">Authentication age already requested by the client.</param>
public sealed record ResourceAuthenticationRequirementContext(
    ClaimsPrincipal Principal,
    string? Resource,
    IReadOnlyList<string> RequestedAcrValues,
    long? MaxAge);

/// <summary>
///     Consumer policy for resource-driven authentication requirements. A null requirement
///     leaves ordinary token validation and voluntary OpenID Connect ACR behavior unchanged.
/// </summary>
public interface IResourceAuthenticationRequirementProvider
{
    /// <summary>Selects the requirements applicable to the current resource and request.</summary>
    Task<ResourceAuthenticationRequirement?> EvaluateAsync(
        ResourceAuthenticationRequirementContext context,
        CancellationToken ct = default);
}
