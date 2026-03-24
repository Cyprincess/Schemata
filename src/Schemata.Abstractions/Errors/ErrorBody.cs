using System.Collections.Generic;

namespace Schemata.Abstractions.Errors;

/// <summary>
///     The body of an error response, containing the error code, message, and structured details.
/// </summary>
public class ErrorBody
{
    /// <summary>
    ///     Gets or sets the error code (e.g., "INVALID_ARGUMENT").
    /// </summary>
    public virtual string? Code { get; set; }

    /// <summary>
    ///     Gets or sets the human-readable error message.
    /// </summary>
    public virtual string? Message { get; set; }

    /// <summary>
    ///     Gets or sets the list of structured error details.
    /// </summary>
    public virtual List<IErrorDetail>? Details { get; set; }
}
