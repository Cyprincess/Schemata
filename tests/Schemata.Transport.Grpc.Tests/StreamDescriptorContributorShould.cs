using System.IO;
using Google.Protobuf;
using System.Linq;
using Schemata.Transport.Grpc;
using Xunit;

namespace Schemata.Transport.Grpc.Tests;

public sealed class StreamDescriptorContributorShould
{
    [Trait("Category", "Integration")]
    [Fact]
    public void Merge_Methods_Registered_Under_The_Same_Service_Into_One_Descriptor() {
        var registry = new StreamDescriptorRegistry();
        registry.Add("schemata.StreamService", "Probe", typeof(ProbeRequest), typeof(ProbeItem));
        registry.Add("schemata.StreamService", "Alternate", typeof(AlternateRequest), typeof(ProbeItem));

        var service = Assert.Single(new StreamDescriptorContributor(registry).GetServiceDescriptors(null!));

        Assert.Equal("StreamService", service.Name);
        Assert.Equal("schemata", service.File.Package);
        Assert.Equal(["Alternate", "Probe"], service.Methods.Select(method => method.Name).OrderBy(name => name));
        var probe = service.Methods.Single(method => method.Name == "Probe");
        var alternate = service.Methods.Single(method => method.Name == "Alternate");
        Assert.Equal(probe.OutputType, alternate.OutputType);
        Assert.NotEqual(probe.InputType, alternate.InputType);
        using var buffer = new MemoryStream();
        registry.Model.Serialize(buffer, new ProbeItem { Value = 37 });
        var input = new CodedInputStream(buffer.ToArray());
        Assert.Equal(WireFormat.MakeTag(probe.OutputType.FindFieldByName("value").FieldNumber, WireFormat.WireType.Varint), input.ReadTag());
        Assert.Equal(37, input.ReadInt32());
        Assert.Equal(0u, input.ReadTag());
    }

    public sealed class ProbeRequest { public string Id { get; set; } = string.Empty; }
    public sealed class AlternateRequest { public string Key { get; set; } = string.Empty; }
    public sealed class ProbeItem { public int Value { get; set; } }
}
