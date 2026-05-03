namespace Schemata.Abstractions.Resource;

/// <summary>
///     Carries a unique client-supplied request identifier for idempotency, per
///     <seealso href="https://google.aip.dev/155">AIP-155: Request identification</seealso>.
/// </summary>
public interface IRequestIdentification
{
    /// <summary>
    ///     The unique, client-assigned request ID for deduplication.
    /// </summary>
    string? RequestId { get; set; }
}
