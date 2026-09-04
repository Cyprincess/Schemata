using System.Collections.Generic;

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

    /// <summary>Client identifier that receives the token.</summary>
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

    /// <summary>Audiences the token is intended for, serialized as a JSON array.</summary>
    public IReadOnlyList<string>? Aud { get; set; }

    /// <summary>
    ///     Granted authorization details, serialized as a JSON array and filtered for the
    ///     introspecting resource server, per
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc9396.html#section-9.2">
    ///         RFC 9396: OAuth 2.0 Rich Authorization
    ///         Requests §9.2: Token Introspection
    ///     </seealso>
    ///     .
    /// </summary>
    public string? AuthorizationDetails { get; set; }

    /// <summary>Issuer of the token.</summary>
    public string? Iss { get; set; }

    /// <summary>Unique identifier for the token.</summary>
    public string? Jti { get; set; }

    /// <summary>
    ///     Confirmation of the proof-of-possession key bound to the token,
    ///     echoed as a top-level JSON object member,
    ///     per
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc9449.html#section-6.2">
    ///         RFC 9449: OAuth 2.0 Demonstrating Proof of Possession (DPoP)
    ///         §6.2: JWK Thumbprint Confirmation Method in Token Introspection
    ///     </seealso>
    ///     .
    /// </summary>
    /// <remarks>Only string members of the stored claim are echoed; DPoP defines <c>jkt</c>.</remarks>
    public Dictionary<string, string>? Cnf { get; set; }

    /// <summary>
    ///     Authentication Context Class Reference satisfied by the user-authentication event
    ///     that produced the token,
    ///     per
    ///     <seealso href="https://www.rfc-editor.org/rfc/rfc9470.html#section-6.2">
    ///         RFC 9470: OAuth 2.0 Step Up Authentication Challenge Protocol
    ///         §6.2: OAuth 2.0 Token Introspection
    ///     </seealso>
    ///     .
    /// </summary>
    public string? Acr { get; set; }

    /// <summary>
    ///     Time the user authentication occurred, as Unix seconds,
    ///     per RFC 9470 §6.2. RFC 9470 defines no <c>amr</c> introspection member, so
    ///     authentication methods are not echoed here.
    /// </summary>
    public long? AuthTime { get; set; }
}
