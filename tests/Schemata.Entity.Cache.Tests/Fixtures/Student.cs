using System;
using Schemata.Abstractions.Entities;

namespace Schemata.Entity.Cache.Tests.Fixtures;

[PrimaryKey(nameof(Uid))]
public class Student : IIdentifier
{
    public string? FullName { get; set; }
    public int     Age      { get; set; }

    #region IIdentifier Members

    public Guid Uid { get; set; }

    #endregion
}
