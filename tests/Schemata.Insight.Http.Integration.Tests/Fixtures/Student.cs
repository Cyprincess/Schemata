using System;
using Schemata.Abstractions.Entities;

namespace Schemata.Insight.Http.Integration.Tests.Fixtures;

[CanonicalName("students/{student}")]
[Microsoft.EntityFrameworkCore.PrimaryKey(nameof(Uid))]
public class Student : IIdentifier, ICanonicalName
{
    public string? FullName { get; set; }
    public int     Age      { get; set; }

    #region ICanonicalName Members

    public string? Name          { get; set; }
    public string? CanonicalName { get; set; }

    #endregion

    #region IIdentifier Members

    public Guid Uid { get; set; }

    #endregion
}
