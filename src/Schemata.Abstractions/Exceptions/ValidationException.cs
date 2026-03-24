using System.Collections.Generic;
using System.Linq;
using Schemata.Abstractions.Errors;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Abstractions.Exceptions;

/// <summary>
///     Thrown when request validation fails, carrying field-level violation details (HTTP 422).
/// </summary>
public sealed class ValidationException : SchemataException
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="ValidationException" /> class with field violations.
    /// </summary>
    /// <param name="errors">The field violations that caused the validation failure.</param>
    /// <param name="status">The HTTP status code.</param>
    /// <param name="code">The machine-readable error code.</param>
    /// <param name="message">The human-readable error message.</param>
    public ValidationException(
        IEnumerable<ErrorFieldViolation> errors,
        int                              status  = 422,
        string?                          code    = ErrorCodes.InvalidArgument,
        string?                          message = null
    ) : base(status, code, message ?? SchemataResources.GetResourceString(SchemataResources.ST1009)) {
        Details = [new BadRequestDetail { FieldViolations = errors.ToList() }];
    }
}
