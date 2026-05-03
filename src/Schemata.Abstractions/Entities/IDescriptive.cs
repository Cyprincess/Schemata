using System.Collections.Generic;

namespace Schemata.Abstractions.Entities;

/// <summary>
///     Provides user-facing display names and descriptions, corresponding
///     to <seealso href="https://google.aip.dev/148">AIP-148: Standard fields</seealso>
///     <c>display_name</c> and <c>description</c>.
/// </summary>
public interface IDescriptive
{
    /// <summary>
    ///     The primary display name, corresponding to AIP-148 <c>display_name</c>.
    /// </summary>
    string? DisplayName { get; set; }

    /// <summary>
    ///     Localized display names keyed by IETF BCP 47 language tag
    ///     (e.g., <c>"en"</c>, <c>"zh-Hans"</c>).
    /// </summary>
    Dictionary<string, string>? DisplayNames { get; set; }

    /// <summary>
    ///     The primary description, corresponding to AIP-148 <c>description</c>.
    /// </summary>
    string? Description { get; set; }

    /// <summary>
    ///     Localized descriptions keyed by IETF BCP 47 language tag
    ///     (e.g., <c>"en"</c>, <c>"zh-Hans"</c>).
    /// </summary>
    Dictionary<string, string>? Descriptions { get; set; }
}
