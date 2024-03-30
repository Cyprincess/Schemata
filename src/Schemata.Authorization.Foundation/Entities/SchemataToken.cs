using System;
using System.ComponentModel.DataAnnotations.Schema;
using Schemata.Abstractions.Entities;

namespace Schemata.Authorization.Foundation.Entities;

[Table("Tokens")]
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

    public virtual DateTime? ExpirationDate { get; set; }

    public virtual DateTime? RedemptionDate { get; set; }

    #region ICanonicalName Members

    public virtual string? Name { get; set; }

    public virtual string? CanonicalName { get; set; }

    #endregion

    #region IConcurrency Members

    public Guid? Timestamp { get; set; }

    #endregion

    #region IIdentifier Members

    public long Id { get; set; }

    #endregion

    #region ITimestamp Members

    public DateTime? CreationDate     { get; set; }
    public DateTime? ModificationDate { get; set; }

    #endregion
}
