using System;
using System.Collections.Generic;
using Schemata.Abstractions.Errors;

namespace Schemata.Abstractions.Exceptions;

/// <summary>
///     Base exception for all Schemata domain errors, carrying HTTP status code, error code, and structured details.
/// </summary>
public class SchemataException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="SchemataException" /> class.
    /// </summary>
    /// <param name="status">The HTTP status code.</param>
    /// <param name="code">The machine-readable error code (e.g., "INVALID_ARGUMENT").</param>
    /// <param name="message">The human-readable error message.</param>
    public SchemataException(int status, string? code = null, string? message = null) : base(message) {
        Status = status;
        Code   = code;
    }

    /// <summary>
    ///     Gets the HTTP status code associated with this error.
    /// </summary>
    public int Status { get; }

    /// <summary>
    ///     Gets the machine-readable error code.
    /// </summary>
    public string? Code { get; }

    /// <summary>
    ///     Gets or sets the structured error details.
    /// </summary>
    public List<IErrorDetail>? Details { get; set; }
}
