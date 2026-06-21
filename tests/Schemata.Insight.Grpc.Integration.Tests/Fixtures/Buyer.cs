using System;
using Microsoft.EntityFrameworkCore;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;

namespace Schemata.Insight.Grpc.Integration.Tests.Fixtures;

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
