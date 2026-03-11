using System;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;

namespace Schemata.Resource.Http.Tests;

public class Student : IIdentifier, ICanonicalName, IConcurrency, IFreshness
{
    public string? FullName { get; set; }

    public int Age { get; set; }

    public int Grade { get; set; }

    #region ICanonicalName Members

    public string? Name { get; set; }

    public string? CanonicalName { get; set; }

    #endregion

    #region IConcurrency Members

    public Guid? Timestamp { get; set; }

    #endregion

    #region IFreshness Members

    public string? EntityTag { get; set; }

    #endregion

    #region IIdentifier Members

    public long Id { get; set; }

    #endregion
}
