using Schemata.Abstractions.Errors;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Abstractions.Exceptions;

/// <summary>
///     One or more request arguments are invalid or malformed.
/// </summary>
/// <remarks>
///     Maps to <c>google.rpc.Code.INVALID_ARGUMENT</c> (HTTP 400), per
///     <seealso href="https://google.aip.dev/193">AIP-193: Errors</seealso>.
///     Attaches <see cref="ErrorReasons.InvalidArgumentValue" /> on
///     <see cref="ErrorInfoDetail" />. Use <see cref="ValidationException" /> when
///     field-level violation details are available.
/// </remarks>
public class InvalidArgumentException : SchemataException
{
    /// <summary>
    ///     Initializes a new <see cref="InvalidArgumentException" />.
    /// </summary>
    /// <param name="code">HTTP response status code.</param>
    /// <param name="status">Canonical error code from <c>google.rpc.Code</c>.</param>
    /// <param name="message">Developer-oriented diagnostic message.</param>
    /// <param name="reason">
    ///     Domain-specific reason attached to <see cref="ErrorInfoDetail.Reason" />.
    ///     Defaults to <see cref="ErrorReasons.InvalidArgumentValue" />.
    /// </param>
    public InvalidArgumentException(
        int     code    = 400,
        string? status  = ErrorCodes.InvalidArgument,
        string? message = null,
        string? reason  = ErrorReasons.InvalidArgumentValue
    ) : base(code, status, message ?? SchemataResources.GetResourceString(SchemataResources.INVALID_ARGUMENT)) {
        if (reason is { Length: > 0 }) {
            Details = [new ErrorInfoDetail { Reason = reason }];
        }
    }
}
