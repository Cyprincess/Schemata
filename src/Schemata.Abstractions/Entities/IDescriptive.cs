using System.Collections.Generic;

namespace Schemata.Abstractions.Entities;

/// <summary>
///     Indicates that an entity has user-facing display names and descriptions.
/// </summary>
public interface IDescriptive
{
    /// <summary>
    ///     Gets or sets the primary display name of the entity.
    /// </summary>
    string? DisplayName { get; set; }

    /// <summary>
    ///     Gets or sets localized display names keyed by culture (e.g. "en", "zh-Hans").
    /// </summary>
    Dictionary<string, string>? DisplayNames { get; set; }

    /// <summary>
    ///     Gets or sets the primary description of the entity.
    /// </summary>
    string? Description { get; set; }

    /// <summary>
    ///     Gets or sets localized descriptions keyed by culture (e.g. "en", "zh-Hans").
    /// </summary>
    Dictionary<string, string>? Descriptions { get; set; }
}
