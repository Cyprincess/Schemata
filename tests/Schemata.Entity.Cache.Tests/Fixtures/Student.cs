using System;
using Schemata.Abstractions.Entities;
using Schemata.Entity.Repository;

namespace Schemata.Entity.Cache.Tests.Fixtures;

public class Student : IIdentifier
{
    public string? FullName { get; set; }
    public int     Age      { get; set; }

    #region IIdentifier Members

    [TableKey]
    public Guid Uid { get; set; }

    #endregion
}
