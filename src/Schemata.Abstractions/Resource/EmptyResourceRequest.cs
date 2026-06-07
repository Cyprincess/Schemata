using Schemata.Abstractions.Entities;

namespace Schemata.Abstractions.Resource;

/// <summary>
///     No-payload request body for AIP-136 custom methods whose semantics are
///     fully captured by the target resource name (e.g. <c>:cancel</c>,
///     <c>:wait</c>). Satisfies the <c>IResourceMethodHandler</c> generic
///     constraint without forcing every verb to declare a dedicated DTO.
/// </summary>
public sealed class EmptyResourceRequest : ICanonicalName
{
    /// <inheritdoc cref="ICanonicalName.Name" />
    public string? Name { get; set; }

    /// <inheritdoc cref="ICanonicalName.CanonicalName" />
    public string? CanonicalName { get; set; }
}
