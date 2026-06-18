using System;
using System.Collections.Generic;
using System.Linq;
using Schemata.Abstractions.Errors;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Abstractions.Exceptions;

/// <summary>
///     Base exception for all Schemata error conditions.
/// </summary>
/// <remarks>
///     Carries an HTTP status code, a canonical <c>google.rpc.Code</c>, and optional typed
///     detail entries per
///     <seealso href="https://google.aip.dev/193">AIP-193: Errors</seealso>.
///     Middleware produces structured error responses from this information
///     without catching individual exception types.
/// </remarks>
public class SchemataException : Exception
{
    /// <summary>
    ///     Initializes a new <see cref="SchemataException" />.
    /// </summary>
    /// <param name="code">
    ///     HTTP response status code returned to the API consumer.
    /// </param>
    /// <param name="status">
    ///     Canonical error code from <c>google.rpc.Code</c> for client-side branching
    ///     (e.g. <c>"NOT_FOUND"</c>).
    /// </param>
    /// <param name="message">
    ///     Developer-oriented diagnostic message; not localized for end-user display.
    /// </param>
    public SchemataException(int code, string? status = null, string? message = null) : base(message) {
        Code   = code;
        Status = status;
    }

    /// <summary>
    ///     HTTP response status code returned to the API consumer.
    /// </summary>
    public int Code { get; }

    /// <summary>
    ///     Canonical error code for client-side branching; drawn from <c>google.rpc.Code</c>
    ///     enum values.
    /// </summary>
    public string? Status { get; }

    /// <summary>
    ///     Typed detail entries providing additional structured information about the error.
    /// </summary>
    public List<IErrorDetail>? Details { get; set; }

    /// <summary>
    ///     Builds the error response envelope returned by the API.
    /// </summary>
    /// <remarks>
    ///     Subclasses may override to produce protocol-specific envelopes — for example,
    ///     <see cref="OAuthException" /> returns an <see cref="OAuthErrorResponse" /> per
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html">RFC 6749: The OAuth 2.0 Authorization Framework</seealso>
    ///     instead of the default <see cref="ErrorResponse" />.
    /// </remarks>
    /// <param name="requestId">Optional request identifier; same semantics as the typed overload.</param>
    /// <param name="domain">Optional ErrorInfo domain.</param>
    public virtual object? CreateErrorResponse(string? requestId = null, string? domain = null) {
        var status  = Status ?? ErrorCodes.Internal;
        var details = new List<IErrorDetail>();

        if (Details is { Count: > 0 }) {
            details.AddRange(Details);
        }

        EnsureErrorInfo(details, status, domain);
        EnsureRequestInfo(details, requestId);

        return new ErrorResponse {
            Error = new() {
                Code    = Code,
                Message = Message,
                Status  = status,
                Details = details,
            },
        };
    }

    protected static void EnsureErrorInfo(List<IErrorDetail> details, string reason, string? domain) {
        if (details.Any(d => d is ErrorInfoDetail)) {
            return;
        }

        details.Insert(0, new ErrorInfoDetail { Reason = reason, Domain = domain, });
    }

    protected static void EnsureRequestInfo(List<IErrorDetail> details, string? requestId) {
        if (string.IsNullOrWhiteSpace(requestId)) {
            return;
        }

        if (details.Any(d => d is RequestInfoDetail)) {
            return;
        }

        details.Add(new RequestInfoDetail { RequestId = requestId });
    }
}
