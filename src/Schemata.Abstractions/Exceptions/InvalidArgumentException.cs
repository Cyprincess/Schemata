using System.Collections.Generic;
using Schemata.Abstractions.Errors;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Abstractions.Exceptions;

/// <summary>
///     One or more request arguments are invalid or malformed.
/// </summary>
/// <remarks>
///     Maps to <c>google.rpc.Code.INVALID_ARGUMENT</c> (HTTP 400), per
///     <seealso href="https://google.aip.dev/193">AIP-193: Errors</seealso>.
///     The default <see cref="ErrorInfoDetail.Reason" /> is
///     <see cref="SchemataResources.INVALID_ARGUMENT" />. Use
///     <see cref="ValidationException" /> when field-level violation details are
///     available.
/// </remarks>
public class InvalidArgumentException : SchemataException
{
    /// <summary>
    ///     Initializes a new <see cref="InvalidArgumentException" /> from a resx key. The
    ///     en-US-invariant message is rendered from <see cref="SchemataResources" /> with
    ///     the named arguments in <paramref name="args" />; <paramref name="resourceKey" />
    ///     also becomes the <see cref="ErrorInfoDetail.Reason" /> so the locale-aware
    ///     response path can rehydrate the localized message from the same template.
    /// </summary>
    /// <param name="resourceKey">
    ///     The <see cref="SchemataResources" /> data name. Defaults to
    ///     <see cref="SchemataResources.INVALID_ARGUMENT" />.
    /// </param>
    /// <param name="args">Optional named arguments substituted into the template.</param>
    public InvalidArgumentException(
        string resourceKey = SchemataResources.INVALID_ARGUMENT,
        IReadOnlyDictionary<string, string?>? args = null
    ) : base(400, ErrorCodes.InvalidArgument, LocalizedMessageFormatter.FormatInvariant(resourceKey, args)) {
        Details = [new ErrorInfoDetail { Reason = resourceKey }];
        AttachMetadata(args);
    }
}
