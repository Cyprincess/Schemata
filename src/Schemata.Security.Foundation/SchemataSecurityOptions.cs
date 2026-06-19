using Schemata.Abstractions;

namespace Schemata.Security.Foundation;

/// <summary>Configures Schemata security services.</summary>
public class SchemataSecurityOptions
{
    /// <summary>Claim type for permission lookup on a ClaimsPrincipal. Default: "role".</summary>
    public string PermissionClaimType { get; set; } = SchemataConstants.Claims.Role;
}
