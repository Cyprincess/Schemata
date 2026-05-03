using System;
using System.Collections.Generic;
using Schemata.Abstractions.Errors;

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
    /// <param name="status">
    ///     HTTP response status code returned to the API consumer.
    /// </param>
    /// <param name="code">
    ///     Canonical error code from <c>google.rpc.Code</c> for client-side branching
    ///     (e.g. <c>"NOT_FOUND"</c>).
    /// </param>
    /// <param name="message">
    ///     Developer-oriented diagnostic message; not localized for end-user display.
    /// </param>
    public SchemataException(int status, string? code = null, string? message = null) : base(message) {
        Status = status;
        Code   = code;
    }

    /// <summary>
    ///     HTTP response status code returned to the API consumer.
    /// </summary>
    public int Status { get; }

    /// <summary>
    ///     Canonical error code for client-side branching; drawn from <c>google.rpc.Code</c>
    ///     enum values.
    /// </summary>
    public string? Code { get; }

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
    /// <param name="details">
    ///     Additional detail entries appended after the exception's own
    ///     <see cref="Details" />.
    /// </param>
    public virtual object? CreateErrorResponse(IEnumerable<IErrorDetail>? details = null) {
        var body = new ErrorBody { Code = Code, Message = Message };

        if (Details is { Count: > 0 }) {
            body.Details = new(Details);
        }

        if (details is not null) {
            body.Details ??= [];
            body.Details.AddRange(details);
        }

        return new ErrorResponse { Error = body };
    }
}
