using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Schemata.Abstractions.Entities;

namespace Schemata.Authorization.Skeleton.Entities;

[DisplayName("Token")]
[Table("SchemataTokens")]
[CanonicalName("tokens/{token}")]
public class SchemataToken : IIdentifier, ICanonicalName, IConcurrency, ITimestamp
{
    public virtual long? ApplicationId { get; set; }

    public virtual long? AuthorizationId { get; set; }

    public virtual string? Subject { get; set; }

    public virtual string? Type { get; set; }

    public virtual string? ReferenceId { get; set; }

    public virtual string? Status { get; set; }

    public virtual string? Payload { get; set; }

    public virtual string? Properties { get; set; }

    public virtual DateTime? ExpireTime { get; set; }

    public virtual DateTime? RedeemTime { get; set; }

    #region ICanonicalName Members

    public virtual string? Name { get; set; }

    public virtual string? CanonicalName { get; set; }

    #endregion

    #region IConcurrency Members

    public virtual Guid? Timestamp { get; set; }

    #endregion

    #region IIdentifier Members

    [Key]
    public virtual long Id { get; set; }

    #endregion

    #region ITimestamp Members

    public virtual DateTime? CreateTime     { get; set; }
    public virtual DateTime? UpdateTime { get; set; }

    #endregion
}
