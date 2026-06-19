using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Abstractions.Exceptions;

/// <summary>
///     The request lacks valid authentication credentials.
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
    /// <param name="code">HTTP response status code.</param>
    /// <param name="status">Canonical error code from <c>google.rpc.Code</c>.</param>
    /// <param name="message">Developer-oriented diagnostic message.</param>
    public UnauthenticatedException(
        int     code    = 401,
        string? status  = ErrorCodes.Unauthenticated,
        string? message = null
    ) : base(code, status, message ?? SchemataResources.GetResourceString(SchemataResources.ST1004)) { }
}
