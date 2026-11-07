using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using ProtoBuf;
using ProtoBuf.Meta;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Flow.Grpc.Wire;
using Schemata.Flow.Integration.Tests.Resource.Fixtures;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;
using Schemata.Resource.Grpc.Runtime;
using Schemata.Transport.Grpc;
using Schemata.Transport.Grpc.Proto;
using Schemata.Transport.Http.Linking;
using Xunit;

namespace Schemata.Flow.Integration.Tests;

[Trait("Category", "Integration")]
public sealed class ProcessDefinitionFilterShould : IClassFixture<WebAppFactory>, IClassFixture<GrpcWebAppFactory>
{
    private readonly WebAppFactory _http;
    private readonly GrpcWebAppFactory _grpc;

    public ProcessDefinitionFilterShould(WebAppFactory http, GrpcWebAppFactory grpc) { _http = http; _grpc = grpc; }

    [Fact]
    public async Task Http_Filters_Ordinally_And_Replays_Process_Definitions_Link() {
        var name = await Register(_http.Services);
        var client = _http.CreateClient();
        Assert.Equal(new[] { $"definitions/{name}/versions/one", $"definitions/{name}/versions/two" },
            await HttpNames(client, $"/v1/processes:definitions?definitionName={name}"));
        var expected = new[] { $"definitions/{name}/versions/two" };
        Assert.Equal(expected, await HttpNames(client, $"/v1/processes:definitions?definitionName={name}&definitionVersion=two"));
        Assert.Empty(await HttpNames(client, $"/v1/processes:definitions?definitionName={name.ToUpperInvariant()}"));
        Assert.Empty(await HttpNames(client, $"/v1/processes:definitions?definitionName={name}&definitionVersion=TWO"));
        var versionOnly = await HttpNames(client, "/v1/processes:definitions?definitionVersion=two");
        Assert.Contains($"definitions/{name}/versions/two", versionOnly);
        Assert.Contains($"definitions/{name}-other/versions/two", versionOnly);
        Assert.DoesNotContain(versionOnly, value => value.EndsWith("/one", StringComparison.Ordinal));
        Assert.Contains(expected[0], await HttpNames(client, "/v1/processes:definitions"));
        var response = await client.GetAsync($"/v1/processes/{name}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var links = LinkHeader.Parse(string.Join(", ", response.Headers.GetValues(LinkHeader.Name)), response.RequestMessage!.RequestUri);
        var link = Assert.Single(links, value => value.Title == "definitions");
        Assert.Contains("definitionName=" + name, link.Target);
        Assert.Contains("definitionVersion=two", link.Target);
        Assert.Equal(expected, await HttpNames(client, link.Target));
        Assert.DoesNotContain(links, value => value.Target.Contains("/definitions/", StringComparison.Ordinal)
            || value.Title is "parent" or "children" || value.Target.Contains("catch", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Grpc_Filters_Ordinally_And_Replays_Full_Definitions_Rpc_With_Inputs() {
        var name = await Register(_grpc.Services);
        var model = RuntimeTypeModel.Create();
        model.DefaultCompatibilityLevel = CompatibilityLevel.Level300;
        SchemataProtoModelConfigurator.ConfigureType(model, typeof(GetRequest));
        SchemataProtoModelConfigurator.ConfigureType(model, typeof(SchemataProcess));
        SchemataProtoModelConfigurator.ConfigureType(model, typeof(ProcessDefinitionInfo));
        SchemataProtoModelConfigurator.ConfigureListTypes(model, [(typeof(ProcessDefinitionInfo), typeof(ProcessDefinitionInfo))]);
        using var channel = _grpc.CreateGrpcChannel();
        var invoker = channel.CreateCallInvoker();
        var definitions = new Method<ListProcessDefinitionsRequest, ListResultBase<ProcessDefinitionInfo, ProcessDefinitionInfo>>(
            MethodType.Unary, "schemata.flow.v1.ProcessDefinitionService", "ListProcessDefinitions",
            GrpcMarshallers.Create<ListProcessDefinitionsRequest>(model),
            GrpcMarshallers.Create<ListResultBase<ProcessDefinitionInfo, ProcessDefinitionInfo>>(model));
        async Task<string[]> Names(string? filter, string? version = null) {
            using var call = invoker.AsyncUnaryCall(definitions, null, new(), new() { DefinitionName = filter, DefinitionVersion = version });
            var result = await call.ResponseAsync;
            // Protobuf omits an empty repeated field; this nullable envelope leaves it unset on read.
            return result.Entities?.Select(row => row.CanonicalName!).OrderBy(value => value, StringComparer.Ordinal).ToArray() ?? [];
        }
        Assert.Equal(new[] { $"definitions/{name}/versions/one", $"definitions/{name}/versions/two" }, await Names(name));
        var expected = new[] { $"definitions/{name}/versions/two" };
        Assert.Equal(expected, await Names(name, "two"));
        Assert.Empty(await Names(name.ToUpperInvariant()));
        Assert.Empty(await Names(name, "TWO"));
        Assert.Empty(await Names(string.Empty));
        var versionOnly = await Names(null, "two");
        Assert.Contains($"definitions/{name}/versions/two", versionOnly);
        Assert.Contains($"definitions/{name}-other/versions/two", versionOnly);
        Assert.DoesNotContain(versionOnly, value => value.EndsWith("/one", StringComparison.Ordinal));
        Assert.Contains(expected[0], await Names(null));
        var descriptor = ResourceNameDescriptor.ForType<SchemataProcess>();
        var get = new Method<GetRequest, SchemataProcess>(MethodType.Unary,
            GrpcResourceNaming.ServiceFullName(typeof(SchemataProcess)), GrpcResourceNaming.MethodName(descriptor, Operations.Get),
            GrpcMarshallers.Create<GetRequest>(model), GrpcMarshallers.Create<SchemataProcess>(model));
        using var process = invoker.AsyncUnaryCall(get, null, new(), new() { CanonicalName = $"processes/{name}" });
        Assert.Equal($"processes/{name}", (await process.ResponseAsync).CanonicalName);
        var links = LinkHeader.Parse(string.Join(", ", process.GetTrailers().Where(entry => entry.Key == "link").Select(entry => entry.Value)));
        var link = Assert.Single(links, value => value.Title == "definitions");
        Assert.Equal("/schemata.flow.v1.ProcessDefinitionService/ListProcessDefinitions", link.Target);
        Assert.Equal(name, link.Extensions!["definition_name"]);
        Assert.Equal("two", link.Extensions["definition_version"]);
        Assert.Equal(expected, await Names(link.Extensions["definition_name"], link.Extensions["definition_version"]));
        Assert.DoesNotContain(links, value => value.Target.Contains("GetDefinition", StringComparison.Ordinal)
            || value.Target.Contains("Catch", StringComparison.Ordinal) || value.Title is "parent" or "children");
    }

    private static async Task<string> Register(IServiceProvider services) {
        var name = "filter-" + Guid.NewGuid().ToString("n");
        var registry = services.GetRequiredService<IProcessRegistry>();
        foreach (var definition in new[] { name, name + "-other" })
            foreach (var version in new[] { "one", "two" })
                await registry.RegisterAsync<ProcessVersionShould.Original>(configure: configuration => {
                    configuration.Name = definition; configuration.Version = version;
                });
        using var scope = services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        database.Processes.Add(new() { Uid = Guid.NewGuid(), Name = name, CanonicalName = $"processes/{name}",
            DefinitionName = name, DefinitionVersion = "two", State = "Running" });
        await database.SaveChangesAsync();
        return name;
    }

    private static async Task<string[]> HttpNames(HttpClient client, string target) {
        using var response = await client.GetAsync(target);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("versions").EnumerateArray()
            .Select(row => row.GetProperty("name").GetString()!).OrderBy(value => value, StringComparer.Ordinal).ToArray();
    }
}
