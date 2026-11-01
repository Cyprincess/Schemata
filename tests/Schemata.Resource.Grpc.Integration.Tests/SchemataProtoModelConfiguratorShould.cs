using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ProtoBuf.Meta;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Common;
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

    [Fact]
    public void List_Result_Field_Names_Follow_The_Entity_Identity() {
        var model = RuntimeTypeModel.Create();
        SchemataProtoModelConfigurator.ConfigureListResultType(model, typeof(StudentEntity), typeof(NamelessSummary));
        var expected = new[] { "students", "total_size", "next_page_token" };
        var fields   = model[typeof(ListResultBase<StudentEntity, NamelessSummary>)].GetFields().Select(f => f.Name)
                                                                                    .OrderBy(n => n, StringComparer.Ordinal);

        Assert.Equal(expected.OrderBy(n => n, StringComparer.Ordinal), fields);
    }

    [Fact]
    public void Shared_Summary_List_Types_Resolve_Per_Entity_Identity() {
        var model = RuntimeTypeModel.Create();
        SchemataProtoModelConfigurator.ConfigureListTypes(model, [
            (typeof(StudentEntity), typeof(NamelessSummary)),
            (typeof(PersonEntity), typeof(NamelessSummary)),
        ]);

        Assert.Contains(model[typeof(ListResultBase<StudentEntity, NamelessSummary>)].GetFields(), f => f.Name == "students");
        Assert.Contains(model[typeof(ListResultBase<PersonEntity, NamelessSummary>)].GetFields(), f => f.Name == "people");
    }

    [Fact]
    public void Http_And_Grpc_Name_The_List_Field_Alike() {
        var listType = typeof(ListResultBase<StudentEntity, NamelessSummary>);
        var http     = JsonNamingPolicy.SnakeCaseLower.ConvertName(
            ResourceWireNameRules.ResolveWireName(listType, nameof(IEntitiesResult<,>.Entities))!);

        var model = RuntimeTypeModel.Create();
        SchemataProtoModelConfigurator.ConfigureListResultType(model, typeof(StudentEntity), typeof(NamelessSummary));

        var fields = model[listType].GetFields().Select(f => f.Name).ToHashSet();

        Assert.Contains(http, fields);
        Assert.Equal("students", http);
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

    [CanonicalName("students/{student}")]
    private sealed class StudentEntity : ICanonicalName
    {
        public string? Name { get; set; }

        public string? CanonicalName { get; set; }
    }

    [CanonicalName("people/{person}")]
    private sealed class PersonEntity : ICanonicalName
    {
        public string? Name { get; set; }

        public string? CanonicalName { get; set; }
    }

    private sealed class NamelessSummary : ICanonicalName
    {
        public string? Name { get; set; }

        public string? CanonicalName { get; set; }
    }
}
