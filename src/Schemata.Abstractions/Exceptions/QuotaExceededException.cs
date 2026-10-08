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
///     The default <see cref="ErrorInfoDetail.Reason" /> is
///     <see cref="SchemataResources.RESOURCE_EXHAUSTED" />; specific quota violations are surfaced
///     through <see cref="QuotaViolation" /> entries supplied to the violations overload.
/// </remarks>
public class QuotaExceededException : SchemataException
{
    /// <summary>
    ///     Initializes a new <see cref="QuotaExceededException" /> from a resx key. The
    ///     en-US-invariant message is rendered from <see cref="SchemataResources" /> with
    ///     the named arguments in <paramref name="args" />; <paramref name="resourceKey" />
    ///     also becomes the <see cref="ErrorInfoDetail.Reason" /> so the locale-aware
    ///     response path can rehydrate the localized message from the same template.
    /// </summary>
    /// <param name="resourceKey">
    ///     The <see cref="SchemataResources" /> data name. Defaults to
    ///     <see cref="SchemataResources.RESOURCE_EXHAUSTED" />.
    /// </param>
    /// <param name="args">Optional named arguments substituted into the template.</param>
    public QuotaExceededException(
        string resourceKey = SchemataResources.RESOURCE_EXHAUSTED,
        IReadOnlyDictionary<string, string?>? args = null
    ) : base(429, ErrorCodes.ResourceExhausted, LocalizedMessageFormatter.FormatInvariant(resourceKey, args)) {
        Details = [new ErrorInfoDetail { Reason = resourceKey }];
        AttachMetadata(args);
    }

    /// <summary>
    ///     Initializes a new <see cref="QuotaExceededException" /> with a list of
    ///     <see cref="QuotaViolation" /> entries packed into a
    ///     <see cref="QuotaFailureDetail" />.
    /// </summary>
    /// <param name="violations">The quotas that were exceeded.</param>
    /// <param name="resourceKey">
    ///     The <see cref="SchemataResources" /> data name. Defaults to
    ///     <see cref="SchemataResources.RESOURCE_EXHAUSTED" />.
    /// </param>
    /// <param name="args">Optional named arguments substituted into the template.</param>
    public QuotaExceededException(
        IEnumerable<QuotaViolation> violations,
        string resourceKey = SchemataResources.RESOURCE_EXHAUSTED,
        IReadOnlyDictionary<string, string?>? args = null
    ) : this(resourceKey, args) {
        Details ??= [];
        Details.Add(new QuotaFailureDetail { Violations = violations.ToList() });
    }
}
