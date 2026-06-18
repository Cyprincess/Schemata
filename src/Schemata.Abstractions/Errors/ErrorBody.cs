using System.Collections.Generic;

namespace Schemata.Abstractions.Errors;

/// <summary>
///     Structured error body per
///     <seealso href="https://google.aip.dev/193">AIP-193: Errors</seealso>
///     HTTP/1.1+JSON representation: an integer HTTP status code, developer-facing message,
///     canonical <c>google.rpc.Code</c> status name, and optional typed detail entries.
/// </summary>
/// <remarks>
///     Field shape follows the AIP-193 wire spec:
///     <c>{ "code": int, "message": string, "status": string, "details": [ ... ] }</c>.
///     The integer <see cref="Code" /> mirrors the HTTP status code; the string
///     <see cref="Status" /> mirrors the canonical <c>google.rpc.Code</c> enum name
///     (e.g. <c>"NOT_FOUND"</c>).
/// </remarks>
public class ErrorBody
{
    /// <summary>
    ///     The HTTP status code that corresponds to <c>google.rpc.Status.code</c>
    ///     (e.g. <c>404</c>, <c>409</c>).
    /// </summary>
    public virtual int Code { get; set; }

    /// <summary>
    ///     Developer-oriented description of the error intended for logging and diagnostics,
    ///     not localized end-user display. Localized messages belong in a
    ///     <see cref="LocalizedMessageDetail" /> entry under <see cref="Details" />.
    /// </summary>
    public virtual string? Message { get; set; }

    /// <summary>
    ///     Canonical enum name from <c>google.rpc.Code</c> for client-side branching
    ///     (e.g. <c>"NOT_FOUND"</c>, <c>"PERMISSION_DENIED"</c>).
    /// </summary>
    public virtual string? Status { get; set; }

    /// <summary>
    ///     Typed detail entries providing additional structured information
    ///     (field violations, quota failures, precondition violations, etc.).
    ///     Per AIP-193, every error response SHOULD include an
    ///     <see cref="ErrorInfoDetail" /> entry so callers can branch on
    ///     <c>(reason, domain)</c> programmatically.
    /// </summary>
    public virtual List<IErrorDetail>? Details { get; set; }
}
