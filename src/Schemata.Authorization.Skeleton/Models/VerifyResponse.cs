using System.Collections.Generic;

namespace Schemata.Authorization.Skeleton.Models;

/// <summary>
///     Response model returned by the device flow verification endpoint.
/// </summary>
public class VerifyResponse
{
    /// <summary>Gets or sets the display name of the requesting application.</summary>
    public virtual string? ApplicationName { get; set; }

    /// <summary>Gets or sets the user code entered for device authorization verification.</summary>
    public virtual string? UserCode { get; set; }

    /// <summary>Gets or sets the scopes the application is requesting access to.</summary>
    public virtual List<ScopeResponse>? Scopes { get; set; }
}
