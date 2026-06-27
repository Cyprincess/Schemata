using System.Collections.Generic;
using System.Linq;
using Schemata.Abstractions.Errors;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Abstractions.Exceptions;

/// <summary>
///     Request validation failed with field-level violation details.
/// </summary>
/// <remarks>
///     Maps to <c>google.rpc.Code.INVALID_ARGUMENT</c> (HTTP 422), per
///     <seealso href="https://google.aip.dev/193">AIP-193: Errors</seealso>.
///     Attaches <see cref="ErrorReasons.ValidationFailed" /> on
///     <see cref="ErrorInfoDetail" />; each field-level violation is surfaced through
///     <see cref="BadRequestDetail.FieldViolations" />.
/// </remarks>
public sealed class ValidationException : SchemataException
{
    /// <summary>
    ///     Initializes a new <see cref="ValidationException" /> carrying field-level violations
    ///     wrapped in a <see cref="BadRequestDetail" />.
    /// </summary>
    /// <param name="errors">Individual field violations that caused the validation failure.</param>
    /// <param name="code">HTTP response status code.</param>
    /// <param name="status">Canonical error code from <c>google.rpc.Code</c>.</param>
    /// <param name="message">Developer-oriented diagnostic message.</param>
    /// <param name="reason">
    ///     Domain-specific reason attached to <see cref="ErrorInfoDetail.Reason" />.
    ///     Defaults to <see cref="ErrorReasons.ValidationFailed" />.
    /// </param>
    public ValidationException(
        IEnumerable<ErrorFieldViolation> errors,
        int                              code    = 422,
        string?                          status  = ErrorCodes.InvalidArgument,
        string?                          message = null,
        string?                          reason  = ErrorReasons.ValidationFailed
    ) : base(code, status, message ?? SchemataResources.GetResourceString(SchemataResources.VALIDATION_ERROR)) {
        Details = reason is { Length: > 0 }
            ? [new ErrorInfoDetail { Reason = reason }, new BadRequestDetail { FieldViolations = errors.ToList() }]
            : [new BadRequestDetail { FieldViolations = errors.ToList() }];
    }
}
