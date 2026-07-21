using Schemata.Abstractions.Entities;

namespace Schemata.Abstractions.Resource;

/// <summary>
///     Collection-scoped request for AIP-165 purge.
/// </summary>
/// <remarks>
///     Purge is executed as a long-running operation. The Resource feature returns
///     an <c>operations/{operation}</c> reference when a Scheduling HTTP or gRPC bridge
///     supplies the operation dispatcher.
/// </remarks>
/// <seealso href="https://google.aip.dev/165">AIP-165: Purge</seealso>
public sealed class PurgeRequest : ICanonicalName, IRequestIdentification
{
    /// <summary>
    ///     AIP filter expression selecting resources to purge. The wildcard <c>*</c>
    ///     selects the whole visible collection.
    /// </summary>
    public string? Filter { get; set; }

    /// <summary>
    ///     The filter expression language; defaults to the resource's first enabled language.
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    ///     Parent resource name that narrows the purge to its child collection.
    /// </summary>
    public string? Parent { get; set; }

    /// <summary>
    ///     When <see langword="true" />, physically removes matching resources. When
    ///     <see langword="false" />, the operation reports a preview count and sample.
    /// </summary>
    public bool Force { get; set; }

    public string? RequestId { get; set; }

    public string? Name { get; set; }

    public string? CanonicalName { get; set; }
}
