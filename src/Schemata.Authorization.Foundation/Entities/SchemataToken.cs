using System;
using Schemata.Abstractions.Entities;

namespace Schemata.Authorization.Foundation.Entities;

public class SchemataToken : IIdentifier, IConcurrency, ITimestamp
{
    public virtual long? ApplicationId { get; set; }

    public virtual long? AuthorizationId { get; set; }

    public virtual string? Subject { get; set; }

    public virtual string? ClientId { get; set; }

    public virtual string? Type { get; set; }

    public virtual string? ReferenceId { get; set; }

    public virtual string? Status { get; set; }

    public virtual DateTime? ExpirationDate { get; set; }

    public virtual DateTime? RedemptionDate { get; set; }

    public virtual string? Payload { get; set; }

    public virtual string? Properties { get; set; }

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
