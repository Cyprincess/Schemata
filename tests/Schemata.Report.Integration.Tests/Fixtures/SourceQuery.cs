using System.Text.Json.Serialization;

namespace Schemata.Report.Integration.Tests.Fixtures;

public sealed class SourceQuery
{
    public int Value { get; set; }
    [JsonIgnore] public int Secret { get; set; }
}
