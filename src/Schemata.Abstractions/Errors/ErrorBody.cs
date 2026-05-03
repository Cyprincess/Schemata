using System.Collections.Generic;

namespace Schemata.Abstractions.Errors;

/// <summary>
///     Structured error body per
///     <seealso href="https://google.aip.dev/193">AIP-193: Errors</seealso>,
///     pairing a machine-readable code with a developer-facing message and
///     optional typed detail entries.
/// </summary>
public class ErrorBody
{
    /// <summary>
    ///     Canonical error code from <c>google.rpc.Code</c> for client-side branching
    ///     (e.g. <c>"NOT_FOUND"</c>, <c>"PERMISSION_DENIED"</c>).
    /// </summary>
    public virtual string? Code { get; set; }

    /// <summary>
    ///     Developer-oriented description of the error intended for logging and diagnostics,
    ///     not localized end-user display.
    /// </summary>
    public virtual string? Message { get; set; }

    /// <summary>
    ///     Typed detail entries providing additional structured information
    ///     (field violations, quota failures, precondition violations, etc.).
    /// </summary>
    public virtual List<IErrorDetail>? Details { get; set; }
}
