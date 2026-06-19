using Schemata.Abstractions.Errors;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Abstractions.Exceptions;

/// <summary>
///     An optimistic concurrency check failed because the resource changed between
///     read and write.
/// </summary>
/// <remarks>
///     Maps to <c>google.rpc.Code.ABORTED</c> (HTTP 409) per
///     <seealso href="https://google.aip.dev/193">AIP-193: Errors</seealso>.
///     Attaches an <see cref="ErrorInfoDetail" /> with reason <c>"CONCURRENCY_MISMATCH"</c>
///     so clients can identify retry-eligible conflicts.
/// </remarks>
public sealed class ConcurrencyException : SchemataException
{
    /// <summary>
    ///     Initializes a new <see cref="ConcurrencyException" />.
    /// </summary>
    /// <param name="code">HTTP response status code.</param>
    /// <param name="status">Canonical error code from <c>google.rpc.Code</c>.</param>
    /// <param name="message">Developer-oriented diagnostic message.</param>
    public ConcurrencyException(
        int     code    = 409,
        string? status  = ErrorCodes.Aborted,
        string? message = null
    ) : base(code, status, message ?? SchemataResources.GetResourceString(SchemataResources.ST1008)) {
        Details = [new ErrorInfoDetail { Reason = ErrorReasons.ConcurrencyMismatch }];
    }
}
