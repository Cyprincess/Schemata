using System.Collections.Generic;
using Schemata.Abstractions.Errors;

namespace Schemata.Abstractions.Exceptions;

/// <summary>
///     Thrown for OAuth 2.0 protocol errors.
/// </summary>
/// <remarks>
///     Produces an <see cref="OAuthErrorResponse" /> per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html">RFC 6749: The OAuth 2.0 Authorization Framework</seealso>
///     instead of the default <see cref="ErrorResponse" />.
/// </remarks>
public class OAuthException : SchemataException
{
    /// <summary>
    ///     Initializes a new <see cref="OAuthException" /> with an OAuth error code and
    ///     description.
    /// </summary>
    /// <param name="error">OAuth 2.0 error code (e.g. <c>"invalid_grant"</c>).</param>
    /// <param name="description">Human-readable explanation of the error.</param>
    /// <param name="status">HTTP response status code.</param>
    public OAuthException(
        string  error,
        string  description,
        int     status = 400
    ) : base(status, error, description) { }

    /// <summary>
    ///     When non-<see langword="null" />, the error is delivered via a redirect to this
    ///     URI rather than a direct JSON response.
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

    /// <summary>
    ///     Returns an <see cref="OAuthErrorResponse" /> instead of the default
    ///     <see cref="ErrorResponse" />.
    /// </summary>
    /// <param name="details">
    ///     Additional detail entries appended after the exception's own
    ///     <see cref="SchemataException.Details" />.
    /// </param>
    public override object? CreateErrorResponse(IEnumerable<IErrorDetail>? details = null) {
        var response = new OAuthErrorResponse {
            Error            = Code,
            ErrorDescription = Message,
            ErrorUri         = ErrorUri,
        };

        if (Details is { Count: > 0 }) {
            response.Details = new(Details);
        }

        if (details is not null) {
            response.Details ??= [];
            response.Details.AddRange(details);
        }

        return response;
    }
}
