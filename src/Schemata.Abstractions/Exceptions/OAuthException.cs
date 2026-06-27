using System.Collections.Generic;
using Schemata.Abstractions.Errors;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Abstractions.Exceptions;

/// <summary>
///     Thrown for OAuth 2.0 protocol errors.
/// </summary>
/// <remarks>
///     Produces an <see cref="OAuthErrorResponse" /> per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html">RFC 6749: The OAuth 2.0 Authorization Framework</seealso>
///     for protocol-specific error serialization.
/// </remarks>
public class OAuthException : SchemataException
{
    /// <summary>
    ///     Initializes a new <see cref="OAuthException" /> with an OAuth error code and
    ///     description.
    /// </summary>
    /// <param name="error">OAuth 2.0 error code (e.g. <c>"invalid_grant"</c>).</param>
    /// <param name="description">Human-readable explanation of the error.</param>
    /// <param name="code">HTTP response status code.</param>
    public OAuthException(
        string  error,
        string  description,
        int     code = 400
    ) : base(code, error, description) { }

    /// <summary>
    ///     Redirect target used to deliver the error through the interactive authorization flow.
    /// </summary>
    /// <remarks>
    ///     Used by the interactive authorization endpoint after <c>redirect_uri</c>
    ///     validation succeeds, per
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-4.1.2.1">
    ///         RFC 6749: The OAuth 2.0 Authorization
    ///         Framework §4.1.2.1: Error Response
    ///     </seealso>
    ///     .
    /// </remarks>
    public string? RedirectUri { get; set; }

    /// <summary>
    ///     Opaque <c>state</c> parameter echoed back to the client during redirect-based
    ///     error delivery, per
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-4.1.2.1">
    ///         RFC 6749: The OAuth 2.0 Authorization
    ///         Framework §4.1.2.1: Error Response
    ///     </seealso>
    ///     .
    /// </summary>
    public string? State { get; set; }

    /// <summary>
    ///     OAuth 2.0 <c>response_mode</c> value controlling how the error is transported
    ///     to the client (e.g. <c>"query"</c>, <c>"fragment"</c>, <c>"form_post"</c>).
    /// </summary>
    public string? ResponseMode { get; set; }

    /// <summary>
    ///     URI identifying a human-readable web page with information about the error,
    ///     per
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-5.2">
    ///         RFC 6749: The OAuth 2.0 Authorization
    ///         Framework §5.2: Error Response
    ///     </seealso>
    ///     .
    /// </summary>
    public string? ErrorUri { get; set; }

    public override object? CreateErrorResponse(string? requestId = null, string? domain = null, string? locale = null) {
        var status  = Status ?? ErrorCodes.Internal;
        var details = new List<IErrorDetail>();

        if (Details is { Count: > 0 }) {
            details.AddRange(Details);
        }

        // The OAuth `error` wire value is RFC 6749 lowercase (e.g. "invalid_grant"); the AIP-193
        // structured Reason / resx data name uses UPPER_SNAKE_CASE. Normalize for ErrorInfoDetail
        // and EnsureLocalizedMessage so OAuth errors localize through the same resx pipeline as
        // every other SchemataException.
        var reason = status.ToUpperInvariant();
        EnsureErrorInfo(details, reason, domain);
        EnsureRequestInfo(details, requestId);
        EnsureLocalizedMessage(details, locale, reason);

        return new OAuthErrorResponse {
            Error            = status,
            ErrorDescription = Message,
            ErrorUri         = ErrorUri,
            Details          = details,
        };
    }
}
