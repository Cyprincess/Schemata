using System;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;

namespace Schemata.Resource.Http.Tests;

public record Student : IIdentifier, IConcurrency, IFreshness
{
    public string? Name { get; set; }

    public int Age { get; set; }

    public int Grade { get; set; }

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
