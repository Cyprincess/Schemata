using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Abstractions.Exceptions;

/// <summary>
///     The system is not in a state required for the operation to proceed.
/// </summary>
/// <remarks>
///     Maps to <c>google.rpc.Code.FAILED_PRECONDITION</c> (HTTP 412), per
///     <seealso href="https://google.aip.dev/193">AIP-193: Errors</seealso>.
/// </remarks>
public class FailedPreconditionException : SchemataException
{
    /// <summary>
    ///     Initializes a new <see cref="FailedPreconditionException" />.
    /// </summary>
    /// <param name="code">HTTP response status code.</param>
    /// <param name="status">Canonical error code from <c>google.rpc.Code</c>.</param>
    /// <param name="message">Developer-oriented diagnostic message.</param>
    public FailedPreconditionException(
        int     code    = 412,
        string? status  = ErrorCodes.FailedPrecondition,
        string? message = null
    ) : base(code, status, message ?? SchemataResources.GetResourceString(SchemataResources.ST1003)) { }
}
