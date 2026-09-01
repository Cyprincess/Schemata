using System;
using System.Collections.Generic;
using Schemata.Abstractions.Entities;

namespace Schemata.Insight.Http.Integration.Tests.Fixtures;

[CanonicalName("customers/{customer}")]
[Microsoft.EntityFrameworkCore.PrimaryKey(nameof(Uid))]
public class Customer : IIdentifier, ICanonicalName
{
    public string?     FullName { get; set; }
    public List<Order> Orders   { get; set; } = [];

    #region ICanonicalName Members

    public string? Name          { get; set; }
    public string? CanonicalName { get; set; }

    #endregion

    #region IIdentifier Members

    public Guid Uid { get; set; }

    #endregion
}