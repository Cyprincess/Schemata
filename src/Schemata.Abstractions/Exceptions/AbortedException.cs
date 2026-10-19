using System.Collections.Generic;
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
///     The default <see cref="ErrorInfoDetail.Reason" /> is
///     <see cref="SchemataResources.CONCURRENCY_MISMATCH" /> so clients can branch on
///     retry-eligible conflicts independently of the top-level <c>ABORTED</c> status.
/// </remarks>
public sealed class AbortedException : SchemataException
{
    /// <summary>
    ///     Initializes a new <see cref="AbortedException" /> from a resx key. The
    ///     en-US-invariant message is rendered from <see cref="SchemataResources" /> with
    ///     the named arguments in <paramref name="args" />; <paramref name="resourceKey" />
    ///     also becomes the <see cref="ErrorInfoDetail.Reason" /> so the locale-aware
    ///     response path can rehydrate the localized message from the same template.
    /// </summary>
    /// <param name="resourceKey">
    ///     The <see cref="SchemataResources" /> data name. Defaults to
    ///     <see cref="SchemataResources.CONCURRENCY_MISMATCH" />.
    /// </param>
    /// <param name="args">Optional named arguments substituted into the template.</param>
    public AbortedException(
        string resourceKey = SchemataResources.CONCURRENCY_MISMATCH,
        IReadOnlyDictionary<string, string?>? args = null
    ) : base(409, ErrorCodes.Aborted, LocalizedMessageFormatter.FormatInvariant(resourceKey, args)) {
        Details = [new ErrorInfoDetail { Reason = resourceKey }];
        AttachMetadata(args);
    }
}
