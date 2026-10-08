using System;
using System.Collections.Generic;
using System.Linq;
using Schemata.Abstractions.Errors;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Abstractions.Exceptions;

/// <summary>
///     The system state blocks the operation.
/// </summary>
/// <remarks>
///     Carries the canonical status <c>google.rpc.Code.FAILED_PRECONDITION</c>, per
///     <seealso href="https://google.aip.dev/193">AIP-193: Errors</seealso>, and defaults to
///     HTTP 412. <c>google.rpc.Code</c> maps <c>FAILED_PRECONDITION</c> to HTTP 400, which is the
///     default <see cref="TenantResolveException" /> uses for the same canonical status.
///     The default <see cref="ErrorInfoDetail.Reason" /> is
///     <see cref="SchemataResources.FAILED_PRECONDITION" />; specific failed predicates are
///     surfaced through <see cref="PreconditionViolation" /> entries supplied to the
///     violations overload.
/// </remarks>
public class FailedPreconditionException : SchemataException
{
    /// <summary>
    ///     Initializes a new <see cref="FailedPreconditionException" /> from a resx key. The
    ///     en-US-invariant message is rendered from
    ///     <see cref="SchemataResources" /> with the named arguments in
    ///     <paramref name="args" />; <paramref name="resourceKey" /> also becomes the
    ///     <see cref="ErrorInfoDetail.Reason" /> so the locale-aware response path can
    ///     rehydrate the localized message from the same template.
    /// </summary>
    /// <param name="resourceKey">
    ///     The <see cref="SchemataResources" /> data name. Defaults to
    ///     <see cref="SchemataResources.FAILED_PRECONDITION" />.
    /// </param>
    /// <param name="args">Optional named arguments substituted into the template.</param>
    /// <param name="innerException">Internal diagnostic cause; excluded from the error response envelope.</param>
    public FailedPreconditionException(
        string resourceKey = SchemataResources.FAILED_PRECONDITION,
        IReadOnlyDictionary<string, string?>? args = null,
        Exception? innerException = null
    ) : base(412, ErrorCodes.FailedPrecondition, LocalizedMessageFormatter.FormatInvariant(resourceKey, args), innerException) {
        Details = [new ErrorInfoDetail { Reason = resourceKey }];
        AttachMetadata(args);
    }

    /// <summary>
    ///     Initializes a new <see cref="FailedPreconditionException" /> with a list of
    ///     <see cref="PreconditionViolation" /> entries packed into a
    ///     <see cref="PreconditionFailureDetail" />.
    /// </summary>
    /// <param name="violations">The preconditions that blocked the operation.</param>
    /// <param name="resourceKey">
    ///     The <see cref="SchemataResources" /> data name. Defaults to
    ///     <see cref="SchemataResources.FAILED_PRECONDITION" />.
    /// </param>
    /// <param name="args">Optional named arguments substituted into the template.</param>
    /// <param name="innerException">Internal diagnostic cause; excluded from the error response envelope.</param>
    public FailedPreconditionException(
        IEnumerable<PreconditionViolation> violations,
        string resourceKey = SchemataResources.FAILED_PRECONDITION,
        IReadOnlyDictionary<string, string?>? args = null,
        Exception? innerException = null
    ) : this(resourceKey, args, innerException) {
        Details ??= [];
        Details.Add(new PreconditionFailureDetail { Violations = violations.ToList() });
    }
}
