using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Schemata.Abstractions.Entities;

namespace Schemata.Authorization.Skeleton.Entities;

/// <summary>
///     Represents an OAuth 2.0 / OpenID Connect token (access token, refresh token, authorization code, or device code).
/// </summary>
/// <remarks>
///     Tokens are bound to an application and optionally an authorization.
///     They carry a payload, have an expiration, and can be revoked or redeemed.
/// </remarks>
[DisplayName("Token")]
[Table("SchemataTokens")]
[CanonicalName("tokens/{token}")]
public class SchemataToken : IIdentifier, ICanonicalName, IConcurrency, ITimestamp
{
    /// <summary>Gets or sets the foreign key to the associated <see cref="SchemataApplication" />.</summary>
    public virtual long? ApplicationId { get; set; }

    /// <summary>Gets or sets the foreign key to the associated <see cref="SchemataAuthorization" />.</summary>
    public virtual long? AuthorizationId { get; set; }

    /// <summary>Gets or sets the subject (user identifier) the token was issued to.</summary>
    public virtual string? Subject { get; set; }

    /// <summary>Gets or sets the token type (e.g. "access_token", "refresh_token", "authorization_code").</summary>
    public virtual string? Type { get; set; }

    /// <summary>Gets or sets the reference identifier used for token lookup when reference tokens are enabled.</summary>
    public virtual string? ReferenceId { get; set; }

    /// <summary>Gets or sets the current status (e.g. "valid", "revoked", "redeemed").</summary>
    public virtual string? Status { get; set; }

    /// <summary>Gets or sets the serialized token payload.</summary>
    public virtual string? Payload { get; set; }

    /// <summary>Gets or sets the JSON-serialized custom properties.</summary>
    public virtual string? Properties { get; set; }

    /// <summary>Gets or sets the UTC expiration time.</summary>
    public virtual DateTime? ExpireTime { get; set; }

    /// <summary>Gets or sets the UTC time when the token was redeemed.</summary>
    public virtual DateTime? RedeemTime { get; set; }

    #region ICanonicalName Members

    /// <inheritdoc />
    public virtual string? Name { get; set; }

    /// <inheritdoc />
    public virtual string? CanonicalName { get; set; }

    #endregion

    #region IConcurrency Members

    /// <inheritdoc />
    public virtual Guid? Timestamp { get; set; }

    #endregion

    #region IIdentifier Members

    /// <inheritdoc />
    [Key]
    public virtual long Id { get; set; }

    #endregion

    #region ITimestamp Members

    /// <inheritdoc />
    public virtual DateTime? CreateTime { get; set; }

    /// <inheritdoc />
    public virtual DateTime? UpdateTime { get; set; }

    #endregion
}
