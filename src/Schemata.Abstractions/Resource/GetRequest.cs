using Schemata.Abstractions.Entities;

namespace Schemata.Abstractions.Resource;

/// <summary>
///     Standard request for retrieving a single resource by name or canonical name,
///     per <seealso href="https://google.aip.dev/131">AIP-131: Standard methods: Get</seealso>.
/// </summary>
public class GetRequest : ICanonicalName
{
    #region ICanonicalName Members

    /// <inheritdoc />
    public string? Name { get; set; }

    /// <inheritdoc />
    public string? CanonicalName { get; set; }

    #endregion
}
