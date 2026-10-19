using System.Text.Json.Serialization;

namespace Schemata.Insight.Grpc.Integration.Tests.Fixtures;

public sealed class BuyerQuery
{
    public int Id { get; set; }
    public string? FullName { get; set; }
    public System.Collections.Generic.List<OrderQuery> Orders { get; set; } = [];
    [JsonIgnore] public string? Secret { get; set; }
}

public sealed class OrderQuery
{
    public string? Status { get; set; }
    public long Amount { get; set; }
    public System.Collections.Generic.Dictionary<string, string> Meta { get; set; } = new();
}
