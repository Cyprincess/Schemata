using System.Collections.Generic;
using Schemata.Abstractions.Errors;

namespace Schemata.Abstractions.Exceptions;

/// <summary>
///     Thrown for OAuth 2.0 protocol errors. Produces RFC 6749 Section 5.2 error responses.
/// </summary>
public class OAuthException : SchemataException
{
    /// <summary>
    ///     Thrown for OAuth 2.0 protocol errors. Produces RFC 6749 Section 5.2 error responses.
    /// </summary>
    public OAuthException(
        string  error,
        string  description,
        int     status = 400
    ) : base(status, error, description) { }

    /// <summary>
    ///     When set, the error is delivered via redirect. Used by interactive endpoints
    ///     (authorize) after redirect_uri validation.
    /// </summary>
    public string? RedirectUri { get; set; }

    /// <summary>OAuth 2.0 state parameter echoed back to the client during redirect-based error delivery.</summary>
    public string? State { get; set; }

    /// <summary>Response mode for error delivery (e.g. "query", "fragment", "form_post").</summary>
    public string? ResponseMode { get; set; }

    /// <inheritdoc />
    public override object? CreateErrorResponse(IEnumerable<IErrorDetail>? details = null) {
        var response = new OAuthErrorResponse { Error = Code, ErrorDescription = Message };

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
