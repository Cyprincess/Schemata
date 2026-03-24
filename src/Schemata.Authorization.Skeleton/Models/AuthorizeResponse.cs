using System.Collections.Generic;

namespace Schemata.Authorization.Skeleton.Models;

/// <summary>
///     Response model returned by the authorization endpoint when user consent is required.
/// </summary>
public class AuthorizeResponse
{
    /// <summary>Gets or sets the display name of the requesting application.</summary>
    public virtual string? ApplicationName { get; set; }

    /// <summary>Gets or sets the scopes the application is requesting access to.</summary>
    public virtual List<ScopeResponse>? Scopes { get; set; }
}
