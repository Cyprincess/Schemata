using System;
using Schemata.Abstractions.Entities;
using Schemata.Entity.Repository;

namespace Schemata.Entity.EntityFrameworkCore.Integration.Tests.Fixtures;

[CanonicalName("students/{student}")]
public class Student : IIdentifier, ICanonicalName, IConcurrency, ISoftDelete, ITimestamp
{
    public string? FullName { get; set; }
    public int     Age      { get; set; }
    public int     Grade    { get; set; }

    #region ICanonicalName Members

    public string? Name          { get; set; }
    public string? CanonicalName { get; set; }

    #endregion

    #region IConcurrency Members

    public Guid? Timestamp { get; set; }

    #endregion

    #region IIdentifier Members

    [TableKey]
    public Guid Uid { get; set; }

    #endregion

    #region ISoftDelete Members

    public DateTime? DeleteTime { get; set; }
    public DateTime? PurgeTime  { get; set; }

    #endregion

    #region ITimestamp Members

    public DateTime? CreateTime { get; set; }
    public DateTime? UpdateTime { get; set; }

    #endregion
}
