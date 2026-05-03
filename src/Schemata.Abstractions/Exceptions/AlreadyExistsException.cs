using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Abstractions.Exceptions;

/// <summary>
///     The resource the client attempted to create already exists.
/// </summary>
/// <remarks>
///     Maps to <c>google.rpc.Code.ALREADY_EXISTS</c> (HTTP 409), per
///     <seealso href="https://google.aip.dev/193">AIP-193: Errors</seealso>.
/// </remarks>
public class AlreadyExistsException : SchemataException
{
    /// <summary>
    ///     Initializes a new <see cref="AlreadyExistsException" />.
    /// </summary>
    /// <param name="status">HTTP response status code.</param>
    /// <param name="code">Canonical error code from <c>google.rpc.Code</c>.</param>
    /// <param name="message">Developer-oriented diagnostic message.</param>
    public AlreadyExistsException(
        int     status  = 409,
        string? code    = ErrorCodes.AlreadyExists,
        string? message = null
    ) : base(status, code, message ?? SchemataResources.GetResourceString(SchemataResources.ST1007)) { }
}
