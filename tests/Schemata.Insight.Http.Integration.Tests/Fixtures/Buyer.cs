using System;
using Microsoft.EntityFrameworkCore;
using Schemata.Abstractions.Entities;

namespace Schemata.Insight.Http.Integration.Tests.Fixtures;

[CanonicalName("buyers/{buyer}")]
[PrimaryKey(nameof(Uid))]
public class Buyer : IIdentifier, ICanonicalName
{
    public int     Id       { get; set; }
    public string? FullName { get; set; }

    #region ICanonicalName Members

    public string? Name          { get; set; }
    public string? CanonicalName { get; set; }

    #endregion

    #region IIdentifier Members

    public Guid Uid { get; set; }

    #endregion
}

[CanonicalName("purchases/{purchase}")]
[PrimaryKey(nameof(Uid))]
public class Purchase : IIdentifier, ICanonicalName
{
    public int     BuyerId { get; set; }
    public int     Amount  { get; set; }
    public string? Status  { get; set; }

    #region ICanonicalName Members

    public string? Name          { get; set; }
    public string? CanonicalName { get; set; }

    #endregion

    #region IIdentifier Members

    public Guid Uid { get; set; }

    #endregion
}
