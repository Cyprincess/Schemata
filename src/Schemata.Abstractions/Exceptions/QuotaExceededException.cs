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
    /// <param name="code">HTTP response status code.</param>
    /// <param name="status">Canonical error code from <c>google.rpc.Code</c>.</param>
    /// <param name="message">Developer-oriented diagnostic message.</param>
    public QuotaExceededException(
        int     code    = 429,
        string? status  = ErrorCodes.ResourceExhausted,
        string? message = null
    ) : base(code, status, message ?? SchemataResources.GetResourceString(SchemataResources.ST1010)) { }
}
