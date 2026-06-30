using System.Collections.Generic;
using System.IO;
using ProtoBuf.Meta;
using Schemata.Transport.Grpc.Proto;
using Xunit;

namespace Schemata.Resource.Grpc.Integration.Tests;

public class SchemataProtoModelConfiguratorShould
{
    [Fact]
    public void Nullable_Map_Value_Omits_Value_Field_And_Roundtrips_As_Empty_String() {
        var model = CreateModel<NullableMapMessage>();
        var value = new NullableMapMessage { Values = new() { ["missing"] = null } };

        var payload = Serialize(model, value);

        Assert.Equal(new byte[] { 0x0a, 0x09, 0x0a, 0x07, 0x6d, 0x69, 0x73, 0x73, 0x69, 0x6e, 0x67 }, payload);

        var result = Deserialize<NullableMapMessage>(model, payload);
        Assert.True(result.Values.TryGetValue("missing", out var actual));
        Assert.Equal(string.Empty, actual);
    }

    [Fact]
    public void Nonnullable_Map_Value_Roundtrips() {
        var model = CreateModel<MapMessage>();
        var value = new MapMessage { Values = new() { ["language"] = "en" } };

        var payload = Serialize(model, value);
        var result  = Deserialize<MapMessage>(model, payload);

        Assert.Equal("en", result.Values["language"]);
    }

    private static RuntimeTypeModel CreateModel<T>() {
        var model = RuntimeTypeModel.Create();
        SchemataProtoModelConfigurator.ConfigureType(model, typeof(T));
        return model;
    }

    private static byte[] Serialize<T>(RuntimeTypeModel model, T value) {
        using var stream = new MemoryStream();
        model.Serialize(stream, value);
        return stream.ToArray();
    }

    private static T Deserialize<T>(RuntimeTypeModel model, byte[] payload) {
        using var stream = new MemoryStream(payload);
        return (T)model.Deserialize(stream, null, typeof(T));
    }

    private sealed class NullableMapMessage
    {
        public Dictionary<string, string?> Values { get; set; } = [];
    }

    private sealed class MapMessage
    {
        public Dictionary<string, string> Values { get; set; } = [];
    }
}
