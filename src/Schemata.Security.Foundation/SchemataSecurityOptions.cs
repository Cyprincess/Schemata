using System;
using Schemata.Abstractions;

namespace Schemata.Security.Foundation;

/// <summary>Configures Schemata security services.</summary>
public class SchemataSecurityOptions
{
    /// <summary>Claim type for permission lookup on a ClaimsPrincipal. Default: "role".</summary>
    public string PermissionClaimType { get; set; } = SchemataConstants.IdentityClaims.Role;

    /// <summary>Cache lifetime for security key rows fetched from a URI (jwks-uri / public-key-uri). Default: 15 minutes.</summary>
    public TimeSpan KeyCacheLifetime { get; set; } = TimeSpan.FromMinutes(15);
}
