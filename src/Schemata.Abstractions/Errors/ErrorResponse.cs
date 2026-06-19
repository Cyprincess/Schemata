namespace Schemata.Abstractions.Errors;

/// <summary>
///     Top-level error-response envelope per
///     <seealso href="https://google.aip.dev/193">AIP-193: Errors</seealso>
///     that wraps an <see cref="ErrorBody" /> for JSON serialization.
/// </summary>
public class ErrorResponse
{
    /// <summary>
    ///     The nested error body. Success and empty responses leave this unset.
    /// </summary>
    public virtual ErrorBody? Error { get; set; }
}
