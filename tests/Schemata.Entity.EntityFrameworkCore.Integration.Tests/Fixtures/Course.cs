using System;
using Schemata.Abstractions.Entities;
using Microsoft.EntityFrameworkCore;

namespace Schemata.Entity.EntityFrameworkCore.Integration.Tests.Fixtures;

[PrimaryKey(nameof(Uid))]
public class Course : IIdentifier, ICanonicalName, ITimestamp
{
    public string? Title   { get; set; }
    public int     Credits { get; set; }

    #region ICanonicalName Members

    public string? Name          { get; set; }
    public string? CanonicalName { get; set; }

    #endregion

    #region IIdentifier Members
    public Guid Uid { get; set; }

    #endregion

    #region ITimestamp Members

    public DateTime? CreateTime { get; set; }
    public DateTime? UpdateTime { get; set; }

    #endregion
}
