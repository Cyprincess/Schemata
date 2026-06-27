using Schemata.Abstractions.Errors;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Abstractions.Exceptions;

/// <summary>
///     The request lacks valid authentication credentials.
/// </summary>
/// <remarks>
///     Maps to <c>google.rpc.Code.UNAUTHENTICATED</c> (HTTP 401), per
///     <seealso href="https://google.aip.dev/193">AIP-193: Errors</seealso>.
///     Attaches <see cref="ErrorReasons.CredentialsMissingOrInvalid" /> on
///     <see cref="ErrorInfoDetail" />.
/// </remarks>
public class UnauthenticatedException : SchemataException
{
    /// <summary>
    ///     Initializes a new <see cref="UnauthenticatedException" />.
    /// </summary>
    /// <param name="code">HTTP response status code.</param>
    /// <param name="status">Canonical error code from <c>google.rpc.Code</c>.</param>
    /// <param name="message">Developer-oriented diagnostic message.</param>
    /// <param name="reason">
    ///     Domain-specific reason attached to <see cref="ErrorInfoDetail.Reason" />.
    ///     Defaults to <see cref="ErrorReasons.CredentialsMissingOrInvalid" />.
    /// </param>
    public UnauthenticatedException(
        int     code    = 401,
        string? status  = ErrorCodes.Unauthenticated,
        string? message = null,
        string? reason  = ErrorReasons.CredentialsMissingOrInvalid
    ) : base(code, status, message ?? SchemataResources.GetResourceString(SchemataResources.UNAUTHENTICATED)) {
        if (reason is { Length: > 0 }) {
            Details = [new ErrorInfoDetail { Reason = reason }];
        }
    }
}
