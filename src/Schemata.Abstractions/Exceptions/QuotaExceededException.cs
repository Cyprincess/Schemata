using System.Collections.Generic;
using System.Linq;
using Schemata.Abstractions.Errors;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Abstractions.Exceptions;

/// <summary>
///     A rate limit or resource quota has been exceeded.
/// </summary>
/// <remarks>
///     Maps to <c>google.rpc.Code.RESOURCE_EXHAUSTED</c> (HTTP 429), per
///     <seealso href="https://google.aip.dev/193">AIP-193: Errors</seealso>.
///     Attaches <see cref="ErrorReasons.QuotaExceeded" /> on
///     <see cref="ErrorInfoDetail" />; specific quota violations are surfaced through
///     <see cref="QuotaViolation" /> entries supplied to the violations overload.
/// </remarks>
public class QuotaExceededException : SchemataException
{
    /// <summary>
    ///     Initializes a new <see cref="QuotaExceededException" />.
    /// </summary>
    /// <param name="code">HTTP response status code.</param>
    /// <param name="status">Canonical error code from <c>google.rpc.Code</c>.</param>
    /// <param name="message">Developer-oriented diagnostic message.</param>
    /// <param name="reason">
    ///     Domain-specific reason attached to <see cref="ErrorInfoDetail.Reason" />.
    ///     Defaults to <see cref="ErrorReasons.QuotaExceeded" />.
    /// </param>
    public QuotaExceededException(
        int     code    = 429,
        string? status  = ErrorCodes.ResourceExhausted,
        string? message = null,
        string? reason  = ErrorReasons.QuotaExceeded
    ) : base(code, status, message ?? SchemataResources.GetResourceString(SchemataResources.RESOURCE_EXHAUSTED)) {
        if (reason is { Length: > 0 }) {
            Details = [new ErrorInfoDetail { Reason = reason }];
        }
    }

    /// <summary>
    ///     Initializes a new <see cref="QuotaExceededException" /> with a list of
    ///     <see cref="QuotaViolation" /> entries packed into a
    ///     <see cref="QuotaFailureDetail" />.
    /// </summary>
    /// <param name="violations">The quotas that were exceeded.</param>
    /// <param name="code">HTTP response status code.</param>
    /// <param name="status">Canonical error code from <c>google.rpc.Code</c>.</param>
    /// <param name="message">Developer-oriented diagnostic message.</param>
    /// <param name="reason">
    ///     Domain-specific reason attached to <see cref="ErrorInfoDetail.Reason" />.
    ///     Defaults to <see cref="ErrorReasons.QuotaExceeded" />.
    /// </param>
    public QuotaExceededException(
        IEnumerable<QuotaViolation> violations,
        int                         code    = 429,
        string?                     status  = ErrorCodes.ResourceExhausted,
        string?                     message = null,
        string?                     reason  = ErrorReasons.QuotaExceeded
    ) : this(code, status, message, reason) {
        Details ??= [];
        Details.Add(new QuotaFailureDetail { Violations = violations.ToList() });
    }
}
