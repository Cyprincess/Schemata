using System.Collections.Generic;

namespace Schemata.Abstractions.Errors;

/// <summary>
///     OAuth 2.0 error response per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc6749.html#section-5.2">
///         RFC 6749: The OAuth 2.0 Authorization
///         Framework §5.2: Error Response
///     </seealso>
///     .
/// </summary>
/// <remarks>
///     The <see cref="Details" /> property extends the standard OAuth fields with structured
///     diagnostics for automated processing.
/// </remarks>
public class OAuthErrorResponse
{
    /// <summary>
    ///     OAuth 2.0 error code (e.g. <c>"invalid_grant"</c>, <c>"unauthorized_client"</c>).
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    ///     Human-readable text assisting the client developer in understanding the error.
    /// </summary>
    public string? ErrorDescription { get; set; }

    /// <summary>
    ///     URI identifying a human-readable web page with information about the error.
    /// </summary>
    public string? ErrorUri { get; set; }

    /// <summary>
    ///     Structured error detail entries for diagnostics and automated processing.
    /// </summary>
    public List<IErrorDetail>? Details { get; set; }
}
