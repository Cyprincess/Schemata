using System;
using System.Collections.Generic;
using Schemata.Abstractions.Errors;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Abstractions.Exceptions;

/// <summary>
///     The resource the client attempted to create already exists.
/// </summary>
/// <remarks>
///     Maps to <c>google.rpc.Code.ALREADY_EXISTS</c> (HTTP 409), per
///     <seealso href="https://google.aip.dev/193">AIP-193: Errors</seealso>.
///     The default <see cref="ErrorInfoDetail.Reason" /> is
///     <see cref="SchemataResources.ALREADY_EXISTS" /> so clients can branch on the
///     domain reason independently of the top-level status.
/// </remarks>
public class AlreadyExistsException : SchemataException
{
    /// <summary>
    ///     Initializes a new <see cref="AlreadyExistsException" /> from a resx key. The
    ///     en-US-invariant message is rendered from <see cref="SchemataResources" /> with
    ///     the named arguments in <paramref name="args" />; <paramref name="resourceKey" />
    ///     also becomes the <see cref="ErrorInfoDetail.Reason" /> so the locale-aware
    ///     response path can rehydrate the localized message from the same template.
    /// </summary>
    /// <param name="resourceKey">
    ///     The <see cref="SchemataResources" /> data name. Defaults to
    ///     <see cref="SchemataResources.ALREADY_EXISTS" />.
    /// </param>
    /// <param name="args">Optional named arguments substituted into the template.</param>
    /// <param name="innerException">Internal provider diagnostic cause.</param>
    public AlreadyExistsException(
        string resourceKey = SchemataResources.ALREADY_EXISTS,
        IReadOnlyDictionary<string, string?>? args = null,
        Exception? innerException = null
    ) : base(409, ErrorCodes.AlreadyExists, LocalizedMessageFormatter.FormatInvariant(resourceKey, args), innerException) {
        Details = [new ErrorInfoDetail { Reason = resourceKey }];
        AttachMetadata(args);
    }
}
