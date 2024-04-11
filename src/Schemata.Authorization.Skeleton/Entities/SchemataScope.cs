using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using Schemata.Abstractions.Entities;

namespace Schemata.Authorization.Skeleton.Entities;

[DisplayName("Scope")]
[Table("SchemataScopes")]
[CanonicalName("scopes/{scope}")]
public class SchemataScope : IIdentifier, ICanonicalName, IDisplayName, IConcurrency, ITimestamp
{
    public virtual string? Description { get; set; }

    public virtual string? Descriptions { get; set; }

    public virtual string? Properties { get; set; }

    public virtual string? Resources { get; set; }

    #region ICanonicalName Members

    public virtual string? Name { get; set; }

    public virtual string? CanonicalName { get; set; }

    #endregion

    #region IConcurrency Members

    public virtual Guid? Timestamp { get; set; }

    #endregion

    #region IDisplayName Members

    public virtual string? DisplayName { get; set; }

    public virtual string? DisplayNames { get; set; }

    #endregion

    #region IIdentifier Members

    public virtual long Id { get; set; }

    #endregion

    #region ITimestamp Members

    public virtual DateTime? CreationDate     { get; set; }
    public virtual DateTime? ModificationDate { get; set; }

    #endregion
}
