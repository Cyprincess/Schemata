using System.Collections.Generic;

namespace Schemata.Abstractions.Errors;

/// <summary>OAuth 2.0 error response per RFC 6749 Section 5.2.</summary>
public class OAuthErrorResponse
{
    /// <summary>OAuth 2.0 error code, e.g. "invalid_grant" or "unauthorized_client".</summary>
    public string? Error { get; set; }

    /// <summary>Human-readable explanation of the error.</summary>
    public string? ErrorDescription { get; set; }

    /// <summary>URI identifying a page with information about the error.</summary>
    public string? ErrorUri { get; set; }

    /// <summary>Schemata extension: structured error details for diagnostics.</summary>
    public List<IErrorDetail>? Details { get; set; }
}
