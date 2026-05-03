namespace Schemata.Abstractions.Errors;

/// <summary>
///     Top-level error-response envelope per
///     <seealso href="https://google.aip.dev/193">AIP-193: Errors</seealso>
///     that wraps an <see cref="ErrorBody" /> for JSON serialization.
/// </summary>
public class ErrorResponse
{
    /// <summary>
    ///     The nested error body; <see langword="null" /> when the response represents a success
    ///     or has no serialized error content.
    /// </summary>
    public virtual ErrorBody? Error { get; set; }
}
