using System;
using System.Collections.Generic;
using Schemata.Abstractions.Entities;

namespace Schemata.Authorization.Foundation.Entities;

public class SchemataScope : IIdentifier, IConcurrency, ITimestamp
{
    public virtual string? Name { get; set; }

    public virtual string? DisplayName { get; set; }

    public virtual string? Description { get; set; }

    public virtual string? Properties { get; set; }

    public virtual List<string>? Resources { get; set; }

    public virtual Dictionary<string, string>? DisplayNames { get; set; }

    public virtual Dictionary<string, string>? Descriptions { get; set; }

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
