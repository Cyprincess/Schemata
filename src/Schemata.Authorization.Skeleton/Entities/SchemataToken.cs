using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using Schemata.Abstractions.Entities;

namespace Schemata.Authorization.Skeleton.Entities;

/// <summary>
///     Represents an OAuth 2.0 token (authorization code, access token, refresh token, or device code).
/// </summary>
[DisplayName("Token")]
[Table("SchemataTokens")]
[CanonicalName("tokens/{token}")]
public class SchemataToken : IIdentifier, ICanonicalName, IConcurrency, ITimestamp, IExpiration
{
    /// <summary>The application this token was issued to.</summary>
    public virtual string? ApplicationName { get; set; }

    /// <summary>The authorization record this token was derived from.</summary>
    public virtual string? AuthorizationName { get; set; }

    /// <summary>Identifier of the resource owner this token represents.</summary>
    public virtual string? Subject { get; set; }

    /// <summary>
    ///     OP session identifier (<c>sid</c>) linking this token to a login session.
    ///     Enables session-aware logout to revoke all tokens associated with a single session.
    /// </summary>
    public virtual string? SessionId { get; set; }

    /// <summary>
    ///     Token type: <c>"access_token"</c>, <c>"refresh_token"</c>, <c>"authorization_code"</c>, or
    ///     <c>"device_code"</c>.
    /// </summary>
    public virtual string? Type { get; set; }

    /// <summary>Lifecycle status: <c>"valid"</c>, <c>"redeemed"</c>, or <c>"revoked"</c>.</summary>
    public virtual string? Status { get; set; }

    /// <summary>
    ///     Serialization format used when the token was issued: <c>"reference"</c>, <c>"jwt"</c>, or <c>"jwe"</c>.
    /// </summary>
    public virtual string? Format { get; set; }

    /// <summary>
    ///     Opaque reference used for token lookup.
    ///     The raw token value is never stored; only this reference persists.
    /// </summary>
    public virtual string? ReferenceId { get; set; }

    /// <summary>Serialized token content (JWT, JSON claims, or encrypted blob depending on <see cref="Format" />).</summary>
    public virtual string? Payload { get; set; }

    #region ICanonicalName Members

    public string? Name          { get; set; }
    public string? CanonicalName { get; set; }

    #endregion

    #region IConcurrency Members

    public virtual Guid? Timestamp { get; set; }

    #endregion

    #region IExpiration Members

    public virtual DateTime? ExpireTime { get; set; }

    #endregion

    #region IIdentifier Members

    public virtual long Id { get; set; }

    #endregion

    #region ITimestamp Members

    public virtual DateTime? CreateTime { get; set; }

    public virtual DateTime? UpdateTime { get; set; }

    #endregion
}
