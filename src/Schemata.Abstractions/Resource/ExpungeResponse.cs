using Schemata.Abstractions.Entities;

namespace Schemata.Abstractions.Resource;

/// <summary>
///     Minimal response returned after an AIP-164 expunge operation.
/// </summary>
/// <seealso href="https://google.aip.dev/164">AIP-164: Soft delete</seealso>
public sealed class ExpungeResponse : ICanonicalName
{
    /// <inheritdoc cref="ICanonicalName.Name" />
    public string? Name { get; set; }

    /// <inheritdoc cref="ICanonicalName.CanonicalName" />
    public string? CanonicalName { get; set; }
}
