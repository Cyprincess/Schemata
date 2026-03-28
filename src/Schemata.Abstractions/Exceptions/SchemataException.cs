using System;
using System.Collections.Generic;
using Schemata.Abstractions.Errors;

namespace Schemata.Abstractions.Exceptions;

/// <summary>
///     Base exception for all Schemata domain errors, carrying HTTP status code, error code, and structured details.
/// </summary>
public class SchemataException : Exception
{
    public SchemataException(int status, string? code = null, string? message = null) : base(message) {
        Status = status;
        Code   = code;
    }

    /// <summary>HTTP status code for the error response.</summary>
    public int Status { get; }

    /// <summary>Machine-readable error code, e.g. "invalid_grant" or "not_found".</summary>
    public string? Code { get; }

    /// <summary>Structured error details for diagnostics and field-level validation messages.</summary>
    public List<IErrorDetail>? Details { get; set; }

    /// <summary>
    ///     Creates the error response body. Subclasses override to produce protocol-specific
    ///     envelopes (e.g., RFC 6749 for OAuth 2.0).
    /// </summary>
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
