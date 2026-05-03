using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Abstractions.Exceptions;

/// <summary>
///     The requested resource or entity was not found.
/// </summary>
/// <remarks>
///     Maps to <c>google.rpc.Code.NOT_FOUND</c> (HTTP 404), per
///     <seealso href="https://google.aip.dev/193">AIP-193: Errors</seealso>.
/// </remarks>
public class NotFoundException : SchemataException
{
    /// <summary>
    ///     Initializes a new <see cref="NotFoundException" />.
    /// </summary>
    /// <param name="status">HTTP response status code.</param>
    /// <param name="code">Canonical error code from <c>google.rpc.Code</c>.</param>
    /// <param name="message">Developer-oriented diagnostic message.</param>
    public NotFoundException(
        int     status  = 404,
        string? code    = ErrorCodes.NotFound,
        string? message = null
    ) : base(status, code, message ?? SchemataResources.GetResourceString(SchemataResources.ST1006)) { }
}
