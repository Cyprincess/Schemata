namespace Schemata.Authorization.Skeleton.Models;

/// <summary>RFC 7662 token introspection response.</summary>
public class IntrospectionResponse
{
    /// <summary>Whether the token is currently valid per RFC 7662 section 2.2.</summary>
    public bool Active { get; set; }

    /// <summary>Space-delimited scopes associated with the token per RFC 7662 section 2.2.</summary>
    public string? Scope { get; set; }

    /// <summary>Client identifier the token was issued to per RFC 7662 section 2.2.</summary>
    public string? ClientId { get; set; }

    /// <summary>Human-readable identifier for the resource owner per RFC 7662 section 2.2.</summary>
    public string? Username { get; set; }

    /// <summary>Type of the introspected token, e.g. "Bearer" per RFC 7662 section 2.2.</summary>
    public string? TokenType { get; set; }

    /// <summary>Expiration time as a Unix timestamp in seconds per RFC 7662 section 2.2.</summary>
    public long? Exp { get; set; }

    /// <summary>Issuance time as a Unix timestamp in seconds per RFC 7662 section 2.2.</summary>
    public long? Iat { get; set; }

    /// <summary>Not-before time as a Unix timestamp in seconds per RFC 7662 section 2.2.</summary>
    public long? Nbf { get; set; }

    /// <summary>Subject identifier of the resource owner per RFC 7662 section 2.2.</summary>
    public string? Sub { get; set; }

    /// <summary>Audience the token is intended for per RFC 7662 section 2.2.</summary>
    public string? Aud { get; set; }

    /// <summary>Issuer of the token per RFC 7662 section 2.2.</summary>
    public string? Iss { get; set; }

    /// <summary>Unique identifier for the token per RFC 7662 section 2.2.</summary>
    public string? Jti { get; set; }
}
