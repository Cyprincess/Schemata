namespace Schemata.Abstractions.Errors;

/// <summary>
///     Top-level error response envelope wrapping an <see cref="ErrorBody" />.
/// </summary>
public class ErrorResponse
{
    /// <summary>
    ///     Gets or sets the error body containing the error details.
    /// </summary>
    public virtual ErrorBody? Error { get; set; }
}
