using Schemata.Abstractions.Entities;

namespace Schemata.Abstractions.Resource;

/// <summary>
///     No-payload response for AIP-136 custom methods and AIP-164 expunge, where the
///     outcome carries no resource body. Satisfies the <c>IResourceMethodHandler</c>
///     <c>TResponse : ICanonicalName</c> constraint while serializing to an empty wire
///     message: the identity members are explicit, so neither System.Text.Json nor the
///     protobuf model surface any property.
/// </summary>
public sealed class EmptyResourceResponse : ICanonicalName
{
    #region ICanonicalName Members

    string? ICanonicalName.Name { get; set; }

    string? ICanonicalName.CanonicalName { get; set; }

    #endregion
}
