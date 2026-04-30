namespace Schemata.Abstractions.Entities;

/// <summary>
///     Indicates that the entity records the principal that owns it (typically the authenticated subject
///     who created the resource). The owner is persisted so ownership survives across requests and can be
///     enforced by authorization advisors.
/// </summary>
public interface IOwnable
{
    /// <summary>
    ///     Gets or sets the canonical name of the principal that owns this resource (e.g., "users/chino").
    /// </summary>
    string? Owner { get; set; }
}
