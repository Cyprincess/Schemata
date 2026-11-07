using System.Text.Json;
using Schemata.Abstractions.Json;
using Schemata.Core.Json;
using Xunit;

namespace Schemata.Core.Tests.Json;

public class PolymorphicTypeResolverShould
{
    [Fact]
    public void RoundTrip_Discovered_Derived_Type_Preserves_Discriminator_And_Value() {
        var options = new JsonSerializerOptions { TypeInfoResolver = PolymorphicTypeResolver.Instance };
        ProbeContract value = new ProbeDetail { Value = "snapshot" };

        var json = JsonSerializer.Serialize(value, options);
        var round = JsonSerializer.Deserialize<ProbeContract>(json, options);

        Assert.Contains("\"@type\":\"quality-probe\"", json);
        var detail = Assert.IsType<ProbeDetail>(round);
        Assert.Equal("snapshot", detail.Value);
    }

    public interface ProbeContract
    {
        string Value { get; }
    }

    [Polymorphic(typeof(ProbeContract), Name = "quality-probe")]
    public sealed class ProbeDetail : ProbeContract
    {
        public string Value { get; set; } = string.Empty;
    }
}
