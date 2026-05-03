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
/// </remarks>
public sealed class ValidationException : SchemataException
{
    /// <summary>
    ///     Initializes a new <see cref="ValidationException" /> carrying field-level violations
    ///     wrapped in a <see cref="BadRequestDetail" />.
    /// </summary>
    /// <param name="errors">Individual field violations that caused the validation failure.</param>
    /// <param name="status">HTTP response status code.</param>
    /// <param name="code">Canonical error code from <c>google.rpc.Code</c>.</param>
    /// <param name="message">Developer-oriented diagnostic message.</param>
    public ValidationException(
        IEnumerable<ErrorFieldViolation> errors,
        int                              status  = 422,
        string?                          code    = ErrorCodes.InvalidArgument,
        string?                          message = null
    ) : base(status, code, message ?? SchemataResources.GetResourceString(SchemataResources.ST1009)) {
        Details = [new BadRequestDetail { FieldViolations = errors.ToList() }];
    }
}
