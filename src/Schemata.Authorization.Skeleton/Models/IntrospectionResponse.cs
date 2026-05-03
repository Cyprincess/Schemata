namespace Schemata.Authorization.Skeleton.Models;

/// <summary>
///     Token introspection response,
///     per
///     <seealso href="https://www.rfc-editor.org/rfc/rfc7662.html#section-2.2">
///         RFC 7662: OAuth 2.0 Token Introspection
///         §2.2: Introspection Response
///     </seealso>
///     .
/// </summary>
public class IntrospectionResponse
{
    /// <summary>Whether the token is currently valid.</summary>
    public bool Active { get; set; }

    /// <summary>Space-delimited scopes associated with the token.</summary>
    public string? Scope { get; set; }

    /// <summary>Client identifier the token was issued to.</summary>
    public string? ClientId { get; set; }

    /// <summary>Human-readable identifier for the resource owner.</summary>
    public string? Username { get; set; }

    /// <summary>Type of the introspected token, e.g. <c>"Bearer"</c>.</summary>
    public string? TokenType { get; set; }

    /// <summary>Expiration time as a Unix timestamp in seconds.</summary>
    public long? Exp { get; set; }

    /// <summary>Issuance time as a Unix timestamp in seconds.</summary>
    public long? Iat { get; set; }

    /// <summary>Not-before time as a Unix timestamp in seconds.</summary>
    public long? Nbf { get; set; }

    /// <summary>Subject identifier of the resource owner.</summary>
    public string? Sub { get; set; }

    /// <summary>Audience the token is intended for.</summary>
    public string? Aud { get; set; }

    /// <summary>Issuer of the token.</summary>
    public string? Iss { get; set; }

    /// <summary>Unique identifier for the token.</summary>
    public string? Jti { get; set; }
}
