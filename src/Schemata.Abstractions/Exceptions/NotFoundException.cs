using System.Collections.Generic;
using Schemata.Abstractions.Errors;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Abstractions.Exceptions;

/// <summary>
///     The requested resource or entity is missing.
/// </summary>
/// <remarks>
///     Maps to <c>google.rpc.Code.NOT_FOUND</c> (HTTP 404), per
///     <seealso href="https://google.aip.dev/193">AIP-193: Errors</seealso>.
///     The default <see cref="ErrorInfoDetail.Reason" /> is
///     <see cref="SchemataResources.NOT_FOUND" />; throw sites with finer context
///     supply a more specific resx key (e.g. <c>USER_NOT_FOUND</c>).
/// </remarks>
public class NotFoundException : SchemataException
{
    /// <summary>
    ///     Initializes a new <see cref="NotFoundException" /> from a resx key. The
    ///     en-US-invariant message is rendered from <see cref="SchemataResources" /> with
    ///     the named arguments in <paramref name="args" />; <paramref name="resourceKey" />
    ///     also becomes the <see cref="ErrorInfoDetail.Reason" /> so the locale-aware
    ///     response path can rehydrate the localized message from the same template.
    /// </summary>
    /// <param name="resourceKey">
    ///     The <see cref="SchemataResources" /> data name. Defaults to
    ///     <see cref="SchemataResources.NOT_FOUND" />.
    /// </param>
    /// <param name="args">Optional named arguments substituted into the template.</param>
    public NotFoundException(
        string resourceKey = SchemataResources.NOT_FOUND,
        IReadOnlyDictionary<string, string?>? args = null
    ) : base(404, ErrorCodes.NotFound, LocalizedMessageFormatter.FormatInvariant(resourceKey, args)) {
        Details = [new ErrorInfoDetail { Reason = resourceKey }];
        AttachMetadata(args);
    }
}
