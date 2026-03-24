namespace Schemata.Abstractions.Resource;

/// <summary>
///     Indicates that a request carries a unique request identifier for idempotency.
/// </summary>
public interface IRequestIdentification
{
    /// <summary>
    ///     Gets or sets the unique request identifier.
    /// </summary>
    string? RequestId { get; set; }
}
