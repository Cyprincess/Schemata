using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Abstractions.Exceptions;

/// <summary>
///     One or more request arguments were invalid or malformed.
/// </summary>
/// <remarks>
///     Maps to <c>google.rpc.Code.INVALID_ARGUMENT</c> (HTTP 400), per
///     <seealso href="https://google.aip.dev/193">AIP-193: Errors</seealso>.
///     Use <see cref="ValidationException" /> when field-level violation details
///     are available.
/// </remarks>
public class InvalidArgumentException : SchemataException
{
    /// <summary>
    ///     Initializes a new <see cref="InvalidArgumentException" />.
    /// </summary>
    /// <param name="status">HTTP response status code.</param>
    /// <param name="code">Canonical error code from <c>google.rpc.Code</c>.</param>
    /// <param name="message">Developer-oriented diagnostic message.</param>
    public InvalidArgumentException(
        int     status  = 400,
        string? code    = ErrorCodes.InvalidArgument,
        string? message = null
    ) : base(status, code, message ?? SchemataResources.GetResourceString(SchemataResources.ST1001)) { }
}
