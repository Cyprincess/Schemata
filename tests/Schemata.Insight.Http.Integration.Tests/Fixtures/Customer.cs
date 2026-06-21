using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;

namespace Schemata.Insight.Http.Integration.Tests.Fixtures;

[CanonicalName("customers/{customer}")]
[PrimaryKey(nameof(Uid))]
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

[PrimaryKey(nameof(Uid))]
public class Order
{
    public Guid    Uid    { get; set; }
    public int     Number { get; set; }
    public string? Status { get; set; }
    public int     Amount { get; set; }
    public int     Placed { get; set; }
}
