using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Abstractions.Exceptions;

/// <summary>
///     The request does not have valid authentication credentials.
/// </summary>
/// <remarks>
///     Maps to <c>google.rpc.Code.UNAUTHENTICATED</c> (HTTP 401), per
///     <seealso href="https://google.aip.dev/193">AIP-193: Errors</seealso>.
/// </remarks>
public class UnauthenticatedException : SchemataException
{
    /// <summary>
    ///     Initializes a new <see cref="UnauthenticatedException" />.
    /// </summary>
    /// <param name="status">HTTP response status code.</param>
    /// <param name="code">Canonical error code from <c>google.rpc.Code</c>.</param>
    /// <param name="message">Developer-oriented diagnostic message.</param>
    public UnauthenticatedException(
        int     status  = 401,
        string? code    = ErrorCodes.Unauthenticated,
        string? message = null
    ) : base(status, code, message ?? SchemataResources.GetResourceString(SchemataResources.ST1004)) { }
}
