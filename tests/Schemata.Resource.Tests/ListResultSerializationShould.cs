using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ProtoBuf.Meta;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Transport.Grpc.Proto;
using Xunit;

namespace Schemata.Resource.Tests;

[Trait("Category", "Integration")]
public class ListResultSerializationShould
{
    [Fact]
    public void Shared_Summary_Uses_Each_Entity_Name_In_Http_And_Grpc() {
        var services = new ServiceCollection();
        services.AddSchemataJsonSerializer(_ => { }, mvc: false);
        services.AddSchemataJsonTraits();
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<JsonSerializerOptions>>().Value;
        var stores = new ListResultBase<Store, Summary> { Entities = [new() { Label = "store" }] };
        var people = new ListResultBase<Person, Summary> { Entities = [new() { Label = "person" }] };

        using var storeJson = JsonDocument.Parse(JsonSerializer.Serialize(stores, options));
        using var personJson = JsonDocument.Parse(JsonSerializer.Serialize(people, options));
        Assert.Equal("store", Assert.Single(storeJson.RootElement.GetProperty("shops").EnumerateArray()).GetProperty("label").GetString());
        Assert.Equal("person", Assert.Single(personJson.RootElement.GetProperty("people").EnumerateArray()).GetProperty("label").GetString());
        Assert.False(storeJson.RootElement.TryGetProperty("wrong", out _));
        Assert.False(personJson.RootElement.TryGetProperty("shops", out _));

        var model = RuntimeTypeModel.Create();
        SchemataProtoModelConfigurator.ConfigureListTypes(model, [(typeof(Store), typeof(Summary)), (typeof(Person), typeof(Summary))]);
        Assert.Equal("shops", model[typeof(ListResultBase<Store, Summary>)].GetFields().Single(f => f.FieldNumber == 1).Name);
        Assert.Equal("people", model[typeof(ListResultBase<Person, Summary>)].GetFields().Single(f => f.FieldNumber == 1).Name);
    }

    [CanonicalName("shops/{shop}")]
    private sealed class Store;

    private sealed class Person;

    [CanonicalName("wrong/{wrong}")]
    public sealed class Summary
    {
        public string? Label { get; set; }
    }
}
