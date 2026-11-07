using System;
using System.Collections.Generic;
using Schemata.Abstractions;
using Schemata.Security.Skeleton;

namespace Schemata.Security.Foundation;

/// <summary>Configures Schemata security services.</summary>
public class SchemataSecurityOptions
{
    /// <summary>Claim type for permission lookup on a ClaimsPrincipal. Default: "role".</summary>
    public string PermissionClaimType { get; set; } = SchemataConstants.IdentityClaims.Role;

    /// <summary>Cache lifetime for security key rows fetched from a URI (jwks-uri / public-key-uri). Default: 15 minutes.</summary>
    public TimeSpan KeyCacheLifetime { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>How long retired keys verify, decrypt, and publish after <see cref="Skeleton.Entities.SchemataSecurity.RetirementTime" />. Default: seven days.</summary>
    public TimeSpan RetiredKeyRetention { get; set; } = TimeSpan.FromDays(7);

    /// <summary>Periodic interval for <see cref="Rotations" />. A null interval leaves rotation unscheduled.</summary>
    public TimeSpan? RotationInterval { get; set; }

    /// <summary>Parent, usage, and algorithm slots rotated by the periodic security job.</summary>
    public List<SecurityKeyRotationOptions> Rotations { get; } = [];
}

/// <summary>Configures proactive rotation for one parent-scoped credential slot.</summary>
public sealed class SecurityKeyRotationOptions
{
    /// <summary>Canonical parent owning the credential.</summary>
    public string Parent { get; set; } = string.Empty;

    /// <summary>Purpose served by the credential. Default: signing.</summary>
    public string Usage { get; set; } = SecurityConstants.Usages.Signing;

    /// <summary>Stored key algorithm. Default: RSA.</summary>
    public string Algorithm { get; set; } = SecurityConstants.Algorithms.Rsa;
}
