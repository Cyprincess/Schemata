using Schemata.Abstractions.Entities;

namespace Schemata.Abstractions.Resource;

/// <summary>
///     Standard request for retrieving a single resource by name or canonical name,
///     per <seealso href="https://google.aip.dev/131">AIP-131: Standard methods: Get</seealso>.
/// </summary>
public class GetRequest : ICanonicalName
{
    /// <summary>
    ///     Comma-separated field paths to include in the response
    ///     per <seealso href="https://google.aip.dev/157">AIP-157: Partial responses</seealso>.
    ///     Omitted or <c>*</c> returns every field. Dot paths traverse nested objects and collection elements.
    /// </summary>
    public string? ReadMask { get; set; }

    #region ICanonicalName Members

    public string? Name { get; set; }

    public string? CanonicalName { get; set; }

    #endregion
}
