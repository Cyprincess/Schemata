namespace Schemata.Authorization.Skeleton.Models;

public class AuthorizeRequest
{
    /// <summary>OAuth 2.0 client identifier per RFC 6749 section 2.2.</summary>
    public string? ClientId { get; set; }

    /// <summary>URI the authorization server redirects to after approval per RFC 6749 section 3.1.2.</summary>
    public string? RedirectUri { get; set; }

    /// <summary>Space-delimited response types, e.g. "code" or "code id_token" per RFC 6749 section 3.1.1.</summary>
    public string? ResponseType { get; set; }

    /// <summary>Space-delimited scopes requested per RFC 6749 section 3.3.</summary>
    public string? Scope { get; set; }

    /// <summary>Opaque value for CSRF protection, returned unchanged in the redirect per RFC 6749 section 4.1.1.</summary>
    public string? State { get; set; }

    /// <summary>String value for replay protection, included in the id_token per OpenID Connect Core section 3.1.2.1.</summary>
    public string? Nonce { get; set; }

    /// <summary>PKCE code challenge derived from the code verifier per RFC 7636 section 4.2.</summary>
    public string? CodeChallenge { get; set; }

    /// <summary>PKCE transformation method, "plain" or "S256" per RFC 7636 section 4.3.</summary>
    public string? CodeChallengeMethod { get; set; }

    /// <summary>Mechanism for returning parameters, e.g. "query", "fragment", "form_post" per OAuth 2.0 MRAR.</summary>
    public string? ResponseMode { get; set; }

    /// <summary>Space-delimited prompt values controlling the authentication UX per OIDC Core section 3.1.2.1.</summary>
    public string? Prompt { get; set; }

    /// <summary>Requested display mode for the authorization page per OIDC Core section 3.1.2.1.</summary>
    public string? Display { get; set; }

    /// <summary>Hint to the authorization server about the end-user's login identifier.</summary>
    public string? LoginHint { get; set; }

    /// <summary>Maximum authentication age in seconds; forces re-authentication if exceeded per OIDC Core.</summary>
    public string? MaxAge { get; set; }

    /// <summary>Actual authentication time (epoch seconds) recorded when the code was issued.</summary>
    public string? AuthTime { get; set; }

    /// <summary>Previously issued id_token for session validation per OIDC Core section 3.1.2.1.</summary>
    public string? IdTokenHint { get; set; }

    /// <summary>Space-delimited requested Authentication Context Class References per OIDC Core section 3.1.2.1.</summary>
    public string? AcrValues { get; set; }
}
