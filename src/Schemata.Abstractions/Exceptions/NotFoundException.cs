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
///     Attaches <see cref="ErrorReasons.ResourceNotFound" /> on <see cref="ErrorInfoDetail" />
///     so clients can branch on the domain reason independently of the top-level status.
/// </remarks>
public class NotFoundException : SchemataException
{
    /// <summary>
    ///     Initializes a new <see cref="NotFoundException" />.
    /// </summary>
    /// <param name="code">HTTP response status code.</param>
    /// <param name="status">Canonical error code from <c>google.rpc.Code</c>.</param>
    /// <param name="message">Developer-oriented diagnostic message.</param>
    /// <param name="reason">
    ///     Domain-specific reason attached to <see cref="ErrorInfoDetail.Reason" />.
    ///     Defaults to <see cref="ErrorReasons.ResourceNotFound" />; throw sites with
    ///     finer context may override (e.g. <c>"USER_NOT_FOUND"</c>).
    /// </param>
    public NotFoundException(
        int     code    = 404,
        string? status  = ErrorCodes.NotFound,
        string? message = null,
        string? reason  = ErrorReasons.ResourceNotFound
    ) : base(code, status, message ?? SchemataResources.GetResourceString(SchemataResources.NOT_FOUND)) {
        if (reason is { Length: > 0 }) {
            Details = [new ErrorInfoDetail { Reason = reason }];
        }
    }

    /// <summary>
    ///     Initializes a new <see cref="NotFoundException" /> from a resx key. The
    ///     en-US-invariant message is rendered from <see cref="SchemataResources" /> with
    ///     the named arguments in <paramref name="args" />; <paramref name="resourceKey" />
    ///     also becomes the <see cref="ErrorInfoDetail.Reason" /> so the locale-aware
    ///     response path can rehydrate the localized message from the same template.
    /// </summary>
    /// <param name="resourceKey">The <see cref="SchemataResources" /> data name.</param>
    /// <param name="args">Optional named arguments substituted into the template.</param>
    public NotFoundException(string resourceKey, IReadOnlyDictionary<string, string?>? args = null)
        : this(message: LocalizedMessageFormatter.FormatInvariant(resourceKey, args), reason: resourceKey) {
        AttachMetadata(args);
    }
}
