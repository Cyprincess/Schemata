using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using Schemata.Abstractions.Entities;

namespace Schemata.Authorization.Skeleton.Entities;

[DisplayName("Token")]
[Table("SchemataTokens")]
[CanonicalName("tokens/{token}")]
public class SchemataToken : IIdentifier, ICanonicalName, IConcurrency, ITimestamp, IExpiration
{
    public virtual string? ApplicationName { get; set; }

    public virtual string? AuthorizationName { get; set; }

    /// <summary>Identifier of the resource owner this token represents.</summary>
    public virtual string? Subject { get; set; }

    /// <summary>OP session identifier (sid) linking this token to a login session for session-aware logout.</summary>
    public virtual string? SessionId { get; set; }

    /// <summary>Token type, e.g. "access_token", "refresh_token", "authorization_code", or "device_code".</summary>
    public virtual string? Type { get; set; }

    /// <summary>Current lifecycle status, e.g. "valid", "redeemed", or "revoked".</summary>
    public virtual string? Status { get; set; }

    /// <summary>Serialization format used when the token was issued: "reference", "jwt", or "jwe".</summary>
    public virtual string? Format { get; set; }

    /// <summary>Opaque reference used for token lookup instead of the raw value.</summary>
    public virtual string? ReferenceId { get; set; }

    /// <summary>Serialized token content (JWT, JSON claims, or encrypted blob depending on format).</summary>
    public virtual string? Payload { get; set; }

    #region ICanonicalName Members

    public string? Name          { get; set; }
    public string? CanonicalName { get; set; }

    #endregion

    #region IConcurrency Members

    /// <inheritdoc />
    public virtual Guid? Timestamp { get; set; }

    #endregion

    #region IExpiration Members

    /// <inheritdoc />
    public virtual DateTime? ExpireTime { get; set; }

    #endregion

    #region IIdentifier Members

    /// <inheritdoc />
    public virtual long Id { get; set; }

    #endregion

    #region ITimestamp Members

    /// <inheritdoc />
    public virtual DateTime? CreateTime { get; set; }

    /// <inheritdoc />
    public virtual DateTime? UpdateTime { get; set; }

    #endregion
}
