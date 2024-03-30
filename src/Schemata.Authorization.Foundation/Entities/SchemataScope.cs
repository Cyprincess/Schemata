using System;
using System.ComponentModel.DataAnnotations.Schema;
using Schemata.Abstractions.Entities;

namespace Schemata.Authorization.Foundation.Entities;

[Table("Scopes")]
[CanonicalName("scopes/{scope}")]
public class SchemataScope : IIdentifier, ICanonicalName, IConcurrency, ITimestamp
{
    public virtual string? DisplayName { get; set; }

    public virtual string? DisplayNames { get; set; }

    public virtual string? Description { get; set; }

    public virtual string? Descriptions { get; set; }

    public virtual string? Properties { get; set; }

    public virtual string? Resources { get; set; }

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
