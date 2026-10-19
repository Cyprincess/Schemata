using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Schemata.Insight.Http.Integration.Tests.Fixtures;

public sealed class ProtectedQuery
{
    public string? Label { get; set; }
    public int Years { get; set; }
    [JsonIgnore] public int Secret { get; set; }
    [JsonIgnore] public string? String { get; set; }
    public List<ProtectedChild> Children { get; set; } = [];
}

public sealed class ProtectedChild
{
    public int Number { get; set; }
    public string? Secret { get; set; }
    public ProtectedDetail? Detail { get; set; }
}

public sealed class ProtectedDetail
{
    public string? Label { get; set; }
    [JsonIgnore] public string? Secret { get; set; }
}
