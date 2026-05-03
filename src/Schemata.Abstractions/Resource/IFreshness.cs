namespace Schemata.Abstractions.Resource;

/// <summary>
///     Supports HTTP ETag / If-Match / If-None-Match freshness validation for
///     optimistic concurrency control, per
///     <seealso href="https://google.aip.dev/154">AIP-154: Resource freshness validation</seealso>.
/// </summary>
public interface IFreshness
{
    /// <summary>
    ///     The entity tag for conditional request matching.
    /// </summary>
    public string? EntityTag { get; set; }
}
