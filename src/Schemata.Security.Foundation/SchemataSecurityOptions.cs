using Schemata.Abstractions;

namespace Schemata.Security.Foundation;

public class SchemataSecurityOptions
{
    /// <summary>Claim type used to look up permissions on a ClaimsPrincipal. Default: "role".</summary>
    public string PermissionClaimType { get; set; } = SchemataConstants.Claims.Role;
}
