using System;
using System.ComponentModel.DataAnnotations.Schema;
using Schemata.Abstractions.Entities;
using Schemata.Entity.Repository;

namespace Schemata.Entity.LinqToDB.Integration.Tests.Fixtures;

[Table("Courses")]
public class Course : IIdentifier, ICanonicalName, ITimestamp
{
    public string? Title   { get; set; }
    public int     Credits { get; set; }

    #region ICanonicalName Members

    public string? Name          { get; set; }
    public string? CanonicalName { get; set; }

    #endregion

    #region IIdentifier Members

    [TableKey]
    public Guid Uid { get; set; }

    #endregion

    #region ITimestamp Members

    public DateTime? CreateTime { get; set; }
    public DateTime? UpdateTime { get; set; }

    #endregion
}
