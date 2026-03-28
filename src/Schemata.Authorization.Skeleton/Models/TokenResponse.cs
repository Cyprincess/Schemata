namespace Schemata.Authorization.Skeleton.Models;

/// <summary>RFC 6749 Section 5.1 token response.</summary>
public sealed class TokenResponse
{
    /// <summary>Issued access token per RFC 6749 section 5.1.</summary>
    public string? AccessToken { get; set; }

    /// <summary>Token type, typically "Bearer" per RFC 6749 section 7.1.</summary>
    public string? TokenType { get; set; }

    /// <summary>Lifetime in seconds of the access token per RFC 6749 section 5.1.</summary>
    public int ExpiresIn { get; set; }

    /// <summary>Refresh token for obtaining new access tokens per RFC 6749 section 5.1.</summary>
    public string? RefreshToken { get; set; }

    /// <summary>ID token issued for OpenID Connect authentication per OIDC Core section 3.1.3.3.</summary>
    public string? IdToken { get; set; }

    /// <summary>Space-delimited scopes associated with the issued token; may differ from the request per RFC 6749 section 5.1.</summary>
    public string? Scope { get; set; }

    /// <summary>Authorization code returned in hybrid flow responses per OIDC Core section 3.3.</summary>
    public string? Code { get; set; }

    /// <summary>RFC 8693 S2.2.1 issued token type.</summary>
    public string? IssuedTokenType { get; set; }
}
