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
///     Attaches <see cref="ErrorReasons.ConcurrencyMismatch" /> on
///     <see cref="ErrorInfoDetail" /> so clients can branch on retry-eligible conflicts
///     independently of the top-level <c>ABORTED</c> status.
/// </remarks>
public sealed class AbortedException : SchemataException
{
    /// <summary>
    ///     Initializes a new <see cref="AbortedException" />.
    /// </summary>
    /// <param name="code">HTTP response status code.</param>
    /// <param name="status">Canonical error code from <c>google.rpc.Code</c>.</param>
    /// <param name="message">Developer-oriented diagnostic message.</param>
    /// <param name="reason">
    ///     Domain-specific reason attached to <see cref="ErrorInfoDetail.Reason" />.
    ///     Defaults to <see cref="ErrorReasons.ConcurrencyMismatch" />.
    /// </param>
    public AbortedException(
        int     code    = 409,
        string? status  = ErrorCodes.Aborted,
        string? message = null,
        string? reason  = ErrorReasons.ConcurrencyMismatch
    ) : base(code, status, message ?? SchemataResources.GetResourceString(SchemataResources.CONCURRENCY_MISMATCH)) {
        if (reason is { Length: > 0 }) {
            Details = [new ErrorInfoDetail { Reason = reason }];
        }
    }
}
