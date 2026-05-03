using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Abstractions.Exceptions;

/// <summary>
///     A rate limit or resource quota has been exceeded.
/// </summary>
/// <remarks>
///     Maps to <c>google.rpc.Code.RESOURCE_EXHAUSTED</c> (HTTP 429), per
///     <seealso href="https://google.aip.dev/193">AIP-193: Errors</seealso>.
/// </remarks>
public class QuotaExceededException : SchemataException
{
    /// <summary>
    ///     Initializes a new <see cref="QuotaExceededException" />.
    /// </summary>
    /// <param name="status">HTTP response status code.</param>
    /// <param name="code">Canonical error code from <c>google.rpc.Code</c>.</param>
    /// <param name="message">Developer-oriented diagnostic message.</param>
    public QuotaExceededException(
        int     status  = 429,
        string? code    = ErrorCodes.ResourceExhausted,
        string? message = null
    ) : base(status, code, message ?? SchemataResources.GetResourceString(SchemataResources.ST1010)) { }
}
