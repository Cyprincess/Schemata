using System.Collections.Generic;
using System.Linq;
using Schemata.Abstractions.Errors;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Abstractions.Exceptions;

/// <summary>
///     The system state blocks the operation.
/// </summary>
/// <remarks>
///     Maps to <c>google.rpc.Code.FAILED_PRECONDITION</c> (HTTP 412), per
///     <seealso href="https://google.aip.dev/193">AIP-193: Errors</seealso>.
///     Attaches <see cref="ErrorReasons.PreconditionNotSatisfied" /> on
///     <see cref="ErrorInfoDetail" />; specific failed predicates are surfaced through
///     <see cref="PreconditionViolation" /> entries supplied to the violations overload.
/// </remarks>
public class FailedPreconditionException : SchemataException
{
    /// <summary>
    ///     Initializes a new <see cref="FailedPreconditionException" />.
    /// </summary>
    /// <param name="code">HTTP response status code.</param>
    /// <param name="status">Canonical error code from <c>google.rpc.Code</c>.</param>
    /// <param name="message">Developer-oriented diagnostic message.</param>
    /// <param name="reason">
    ///     Domain-specific reason attached to <see cref="ErrorInfoDetail.Reason" />.
    ///     Defaults to <see cref="ErrorReasons.PreconditionNotSatisfied" />.
    /// </param>
    public FailedPreconditionException(
        int     code    = 412,
        string? status  = ErrorCodes.FailedPrecondition,
        string? message = null,
        string? reason  = ErrorReasons.PreconditionNotSatisfied
    ) : base(code, status, message ?? SchemataResources.GetResourceString(SchemataResources.FAILED_PRECONDITION)) {
        if (reason is { Length: > 0 }) {
            Details = [new ErrorInfoDetail { Reason = reason }];
        }
    }

    /// <summary>
    ///     Initializes a new <see cref="FailedPreconditionException" /> with a list of
    ///     <see cref="PreconditionViolation" /> entries packed into a
    ///     <see cref="PreconditionFailureDetail" />.
    /// </summary>
    /// <param name="violations">The preconditions that blocked the operation.</param>
    /// <param name="code">HTTP response status code.</param>
    /// <param name="status">Canonical error code from <c>google.rpc.Code</c>.</param>
    /// <param name="message">Developer-oriented diagnostic message.</param>
    /// <param name="reason">
    ///     Domain-specific reason attached to <see cref="ErrorInfoDetail.Reason" />.
    ///     Defaults to <see cref="ErrorReasons.PreconditionNotSatisfied" />.
    /// </param>
    public FailedPreconditionException(
        IEnumerable<PreconditionViolation> violations,
        int                                code    = 412,
        string?                            status  = ErrorCodes.FailedPrecondition,
        string?                            message = null,
        string?                            reason  = ErrorReasons.PreconditionNotSatisfied
    ) : this(code, status, message, reason) {
        Details ??= [];
        Details.Add(new PreconditionFailureDetail { Violations = violations.ToList() });
    }

    /// <summary>
    ///     Initializes a new <see cref="FailedPreconditionException" /> from a resx key. The
    ///     en-US-invariant message is rendered from
    ///     <see cref="SchemataResources" /> with the named arguments in
    ///     <paramref name="args" />; <paramref name="resourceKey" /> also becomes the
    ///     <see cref="ErrorInfoDetail.Reason" /> so the locale-aware response path can
    ///     rehydrate the localized message from the same template.
    /// </summary>
    /// <param name="resourceKey">The <see cref="SchemataResources" /> data name.</param>
    /// <param name="args">Optional named arguments substituted into the template.</param>
    public FailedPreconditionException(string resourceKey, IReadOnlyDictionary<string, string?>? args = null)
        : this(message: LocalizedMessageFormatter.FormatInvariant(resourceKey, args), reason: resourceKey) {
        AttachMetadata(args);
    }
}
