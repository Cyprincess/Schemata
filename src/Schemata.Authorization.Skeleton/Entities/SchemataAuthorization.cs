using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using Schemata.Abstractions.Entities;

namespace Schemata.Authorization.Skeleton.Entities;

[DisplayName("Authorization")]
[Table("SchemataAuthorizations")]
[CanonicalName("authorizations/{authorization}")]
public class SchemataAuthorization : IIdentifier, ICanonicalName, IConcurrency, ITimestamp
{
    public virtual long? ApplicationId { get; set; }

    public virtual string? Subject { get; set; }

    public virtual string? Type { get; set; }

    public virtual string? Status { get; set; }

    public virtual string? Properties { get; set; }

    public virtual string? Scopes { get; set; }

    #region ICanonicalName Members

    public virtual string? Name { get; set; }

    public virtual string? CanonicalName { get; set; }

    #endregion

    #region IConcurrency Members

    public virtual Guid? Timestamp { get; set; }

    #endregion

    #region IIdentifier Members

    public virtual long Id { get; set; }

    #endregion

    #region ITimestamp Members

    public virtual DateTime? CreationDate     { get; set; }
    public virtual DateTime? ModificationDate { get; set; }

    #endregion
}
