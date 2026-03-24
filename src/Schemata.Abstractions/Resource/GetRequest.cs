using Schemata.Abstractions.Entities;

namespace Schemata.Abstractions.Resource;

/// <summary>
///     Standard request parameters for retrieving a single resource by name.
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
