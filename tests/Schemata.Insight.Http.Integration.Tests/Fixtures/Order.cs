using System;

namespace Schemata.Insight.Http.Integration.Tests.Fixtures;

[Microsoft.EntityFrameworkCore.PrimaryKey(nameof(Uid))]
public class Order
{
    public Guid    Uid    { get; set; }
    public int     Number { get; set; }
    public string? Status { get; set; }
    public int     Amount { get; set; }
    public int     Placed { get; set; }
}