using Schemata.Abstractions.Entities;

namespace Schemata.Entity.Cache.Tests.Fixtures;

public class Student : IIdentifier
{
    public string? FullName { get; set; }
    public int     Age      { get; set; }

    #region IIdentifier Members

    public long Id { get; set; }

    #endregion
}
