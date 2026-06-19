using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Abstractions.Exceptions;

/// <summary>
///     The caller lacks permission to execute the operation.
/// </summary>
/// <remarks>
///     Maps to <c>google.rpc.Code.PERMISSION_DENIED</c> (HTTP 403), per
///     <seealso href="https://google.aip.dev/193">AIP-193: Errors</seealso>.
/// </remarks>
public class AuthorizationException : SchemataException
{
    /// <summary>
    ///     Initializes a new <see cref="AuthorizationException" />.
    /// </summary>
    /// <param name="code">HTTP response status code.</param>
    /// <param name="status">Canonical error code from <c>google.rpc.Code</c>.</param>
    /// <param name="message">Developer-oriented diagnostic message.</param>
    public AuthorizationException(
        int     code    = 403,
        string? status  = ErrorCodes.PermissionDenied,
        string? message = null
    ) : base(code, status, message ?? SchemataResources.GetResourceString(SchemataResources.ST1005)) { }
}
