using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;

namespace Schemata.Security.Skeleton.Entities;

/// <summary>
///     A unified token row: OAuth 2.0 tokens (authorization code, access token, refresh token, or device
///     code) and key-value slots (nonce, jti replay marker, rate-limit window), per the ASP.NET Core
///     Identity <c>AspNetUserTokens</c> analogy.
/// </summary>
[Table("SchemataTokens")]
[CanonicalName("tokens/{token}")]
[PrimaryKey(nameof(Uid))]
[Index(nameof(Parent), nameof(Provider), nameof(Name), IsUnique = true)]
[Index(nameof(Type), nameof(Status))]
[Index(nameof(ReferenceId), IsUnique = true)]
public class SchemataToken : IIdentifier, ICanonicalName, IConcurrency, ITimestamp, IExpiration
{
    /// <summary>Polymorphic owner reference: canonical name of any host resource (users/{x}, applications/{y}, issuer URI).</summary>
    [ResourceReference]
    public virtual string? Parent { get; set; }

    /// <summary>Canonical name of the application that receives this token.</summary>
    [ResourceReference]
    public virtual string? Application { get; set; }

    /// <summary>Canonical name of the authorization record that grants this token.</summary>
    [ResourceReference]
    public virtual string? Authorization { get; set; }

    /// <summary>Issuing or managing subsystem (authorization, dpop, device, assertion…); the AspNet <c>LoginProvider</c> analogue.</summary>
    public virtual string? Provider { get; set; }

    /// <summary>
    ///     OP session identifier (<c>sid</c>) linking this token to a login session.
    ///     Enables session-aware logout to revoke all tokens associated with a single session.
    /// </summary>
    public virtual string? SessionId { get; set; }

    /// <summary>
    ///     Token type: <c>"access_token"</c>, <c>"refresh_token"</c>, <c>"authorization_code"</c>,
    ///     <c>"device_code"</c>, or a slot type (<c>"nonce"</c>, <c>"jti"</c>, <c>"rate-slot"</c>).
    /// </summary>
    public virtual string? Type { get; set; }

    /// <summary>Lifecycle status: <c>"valid"</c>, <c>"redeemed"</c>, or <c>"revoked"</c>; slot rows carry no status.</summary>
    public virtual string? Status { get; set; }

    /// <summary>
    ///     Serialization format for this token: <c>"reference"</c>, <c>"jwt"</c>, or <c>"jwe"</c>.
    /// </summary>
    public virtual string? Format { get; set; }

    /// <summary>
    ///     Opaque reference used for token lookup.
    ///     Only this reference persists for opaque tokens.
    /// </summary>
    public virtual string? ReferenceId { get; set; }

    /// <summary>Serialized token content (JWT or JSON claims depending on <see cref="Format" />).</summary>
    public virtual string? Payload { get; set; }

    /// <summary>Slot payload (nonce value, jti marker, rate-limit count); <see cref="Payload" /> holds OAuth private data.</summary>
    public virtual string? Value { get; set; }

    #region ICanonicalName Members

    /// <summary>Slot name or type refinement (<c>nonce</c>, <c>jti:{id}</c>, <c>rate:{key}</c>); the AspNet <c>Name</c> analogue.</summary>
    public string? Name { get; set; }

    public string? CanonicalName { get; set; }

    #endregion

    #region IConcurrency Members

    [ConcurrencyCheck]
    public virtual Guid Timestamp { get; set; }

    #endregion

    #region IExpiration Members

    public virtual DateTime? ExpireTime { get; set; }

    #endregion

    #region IIdentifier Members

    public virtual Guid Uid { get; set; }

    #endregion

    #region ITimestamp Members

    public virtual DateTime? CreateTime { get; set; }

    public virtual DateTime? UpdateTime { get; set; }

    #endregion
}
