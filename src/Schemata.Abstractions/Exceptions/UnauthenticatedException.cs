using System.Collections.Generic;
using Schemata.Abstractions.Errors;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Abstractions.Exceptions;

/// <summary>
///     The request lacks valid authentication credentials.
/// </summary>
/// <remarks>
///     Maps to <c>google.rpc.Code.UNAUTHENTICATED</c> (HTTP 401), per
///     <seealso href="https://google.aip.dev/193">AIP-193: Errors</seealso>.
///     The default <see cref="ErrorInfoDetail.Reason" /> is
///     <see cref="SchemataResources.UNAUTHENTICATED" />.
/// </remarks>
public class UnauthenticatedException : SchemataException
{
    /// <summary>
    ///     Initializes a new <see cref="UnauthenticatedException" /> from a resx key. The
    ///     en-US-invariant message is rendered from <see cref="SchemataResources" /> with
    ///     the named arguments in <paramref name="args" />; <paramref name="resourceKey" />
    ///     also becomes the <see cref="ErrorInfoDetail.Reason" /> so the locale-aware
    ///     response path can rehydrate the localized message from the same template.
    /// </summary>
    /// <param name="resourceKey">
    ///     The <see cref="SchemataResources" /> data name. Defaults to
    ///     <see cref="SchemataResources.UNAUTHENTICATED" />.
    /// </param>
    /// <param name="args">Optional named arguments substituted into the template.</param>
    public UnauthenticatedException(
        string resourceKey = SchemataResources.UNAUTHENTICATED,
        IReadOnlyDictionary<string, string?>? args = null
    ) : base(401, ErrorCodes.Unauthenticated, LocalizedMessageFormatter.FormatInvariant(resourceKey, args)) {
        Details = [new ErrorInfoDetail { Reason = resourceKey }];
        AttachMetadata(args);
    }
}
