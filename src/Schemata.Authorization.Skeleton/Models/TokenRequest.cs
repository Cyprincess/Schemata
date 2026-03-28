namespace Schemata.Authorization.Skeleton.Models;

public class TokenRequest
{
    /// <summary>Grant type identifier, e.g. "authorization_code" or "refresh_token" per RFC 6749 section 4.1.3.</summary>
    public string? GrantType { get; set; }

    /// <summary>OAuth 2.0 client identifier per RFC 6749 section 2.2.</summary>
    public string? ClientId { get; set; }

    /// <summary>Client secret for confidential clients, sent in the request body per RFC 6749 section 2.3.1.</summary>
    public string? ClientSecret { get; set; }

    /// <summary>Authorization code received from the authorization endpoint per RFC 6749 section 4.1.3.</summary>
    public string? Code { get; set; }

    /// <summary>PKCE code verifier matching the code challenge sent in the authorize request per RFC 7636 section 4.5.</summary>
    public string? CodeVerifier { get; set; }

    /// <summary>Redirect URI that was used in the original authorization request, for validation per RFC 6749 section 4.1.3.</summary>
    public string? RedirectUri { get; set; }

    /// <summary>Space-delimited scopes requested; semantics vary by grant type per RFC 6749 section 3.3.</summary>
    public string? Scope { get; set; }

    /// <summary>Refresh token for the refresh_token grant per RFC 6749 section 6.</summary>
    public string? RefreshToken { get; set; }

    /// <summary>Device code for the urn:ietf:params:oauth:grant-type:device_code grant per RFC 8628 section 3.4.</summary>
    public string? DeviceCode { get; set; }

    /// <summary>Security token representing the subject of the exchange per RFC 8693 section 2.1.</summary>
    public string? SubjectToken { get; set; }

    /// <summary>URI identifying the type of the subject token per RFC 8693 section 2.1.</summary>
    public string? SubjectTokenType { get; set; }

    /// <summary>Security token representing the actor in delegation scenarios per RFC 8693 section 2.1.</summary>
    public string? ActorToken { get; set; }

    /// <summary>URI identifying the type of the actor token per RFC 8693 section 2.1.</summary>
    public string? ActorTokenType { get; set; }
}
