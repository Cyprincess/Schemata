namespace Schemata.Abstractions.Resource;

/// <summary>
///     Indicates that a request or response supports HTTP ETag-based freshness validation.
/// </summary>
public interface IFreshness
{
    /// <summary>
    ///     Gets or sets the entity tag used for conditional requests (If-Match / If-None-Match).
    /// </summary>
    public string? EntityTag { get; set; }
}
