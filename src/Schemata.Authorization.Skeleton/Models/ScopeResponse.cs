namespace Schemata.Authorization.Skeleton.Models;

/// <summary>
///     Response model describing a single OAuth 2.0 scope for consent screens.
/// </summary>
public class ScopeResponse
{
    /// <summary>Gets or sets the scope identifier.</summary>
    public virtual string? Name { get; set; }

    /// <summary>Gets or sets the localized display name.</summary>
    public virtual string? DisplayName { get; set; }

    /// <summary>Gets or sets the localized description.</summary>
    public virtual string? Description { get; set; }
}
