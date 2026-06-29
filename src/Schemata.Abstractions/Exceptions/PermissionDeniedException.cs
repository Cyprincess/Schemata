using System.Collections.Generic;
using Schemata.Abstractions.Errors;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Abstractions.Exceptions;

/// <summary>
///     The caller lacks permission to execute the operation.
/// </summary>
/// <remarks>
///     Maps to <c>google.rpc.Code.PERMISSION_DENIED</c> (HTTP 403), per
///     <seealso href="https://google.aip.dev/193">AIP-193: Errors</seealso>.
///     Attaches <see cref="ErrorReasons.InsufficientPermission" /> on
///     <see cref="ErrorInfoDetail" />.
/// </remarks>
public class PermissionDeniedException : SchemataException
{
    /// <summary>
    ///     Initializes a new <see cref="PermissionDeniedException" />.
    /// </summary>
    /// <param name="code">HTTP response status code.</param>
    /// <param name="status">Canonical error code from <c>google.rpc.Code</c>.</param>
    /// <param name="message">Developer-oriented diagnostic message.</param>
    /// <param name="reason">
    ///     Domain-specific reason attached to <see cref="ErrorInfoDetail.Reason" />.
    ///     Defaults to <see cref="ErrorReasons.InsufficientPermission" />.
    /// </param>
    public PermissionDeniedException(
        int     code    = 403,
        string? status  = ErrorCodes.PermissionDenied,
        string? message = null,
        string? reason  = ErrorReasons.InsufficientPermission
    ) : base(code, status, message ?? SchemataResources.GetResourceString(SchemataResources.PERMISSION_DENIED)) {
        if (reason is { Length: > 0 }) {
            Details = [new ErrorInfoDetail { Reason = reason }];
        }
    }

    /// <summary>
    ///     Initializes a new <see cref="PermissionDeniedException" /> from a resx key. The
    ///     en-US-invariant message is rendered from <see cref="SchemataResources" /> with
    ///     the named arguments in <paramref name="args" />; <paramref name="resourceKey" />
    ///     also becomes the <see cref="ErrorInfoDetail.Reason" /> so the locale-aware
    ///     response path can rehydrate the localized message from the same template.
    /// </summary>
    /// <param name="resourceKey">The <see cref="SchemataResources" /> data name.</param>
    /// <param name="args">Optional named arguments substituted into the template.</param>
    public PermissionDeniedException(string resourceKey, IReadOnlyDictionary<string, string>? args = null)
        : this(message: LocalizedMessageFormatter.FormatInvariant(resourceKey, args), reason: resourceKey) {
        AttachMetadata(args);
    }
}
