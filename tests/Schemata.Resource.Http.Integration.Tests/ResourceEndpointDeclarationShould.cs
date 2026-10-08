using System;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf.Reflection;
using Grpc.Core;
using Grpc.Net.Client;
using Grpc.Reflection.V1Alpha;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using ProtoBuf.Grpc.Client;
using ProtoBuf.Grpc.Configuration;
using Schemata.Resource.Grpc;
using Schemata.Resource.Http.Integration.Tests.Fixtures;
using Xunit;

namespace Schemata.Resource.Http.Integration.Tests;

[Trait("Category", "Integration")]
public class ResourceEndpointDeclarationShould : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;

    public ResourceEndpointDeclarationShould(WebAppFactory factory) { _factory = factory; }

    [Trait("Layer", "Integration")]
    [Fact]
    public async Task HttpDeclaration_AddResourceExposesHttp_WithoutRpcOrReflection() {
        using var factory = Configure();
        using var http = factory.CreateClient(new() { BaseAddress = new("http://localhost") });
        using var channel = GrpcChannel.ForAddress(http.BaseAddress!, new() { HttpClient = http });

        using var created = await http.PostAsJsonAsync("/v1/httpNotes", new { label = "HTTP declaration" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        var name = body.GetProperty("name").GetString();
        Assert.StartsWith("httpNotes/consumer-", name);
        using var fetched = await http.GetAsync($"/v1/{name}");
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
        Assert.Equal("HTTP declaration", (await fetched.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("label").GetString());

        var service = $"{typeof(HttpNote).Namespace}.HttpNoteService";
        var marshaller = new Marshaller<byte[]>(bytes => bytes, bytes => bytes);
        var method = new Method<byte[], byte[]>(MethodType.Unary, service, "ListHttpNotes", marshaller, marshaller);
        using var call = channel.CreateCallInvoker().AsyncUnaryCall(method, null, new CallOptions(), Array.Empty<byte>());
        var error = await Assert.ThrowsAsync<RpcException>(() => call.ResponseAsync);
        Assert.Equal(StatusCode.Unimplemented, error.StatusCode);

        var reflection = new ServerReflection.ServerReflectionClient(channel);
        using var services = reflection.ServerReflectionInfo();
        await services.RequestStream.WriteAsync(new() { ListServices = "" });
        await services.RequestStream.CompleteAsync();
        Assert.True(await services.ResponseStream.MoveNext(CancellationToken.None));
        Assert.DoesNotContain(services.ResponseStream.Current.ListServicesResponse.Service, entry => entry.Name == service);
        using var descriptor = reflection.ServerReflectionInfo();
        await descriptor.RequestStream.WriteAsync(new() { FileContainingSymbol = service });
        await descriptor.RequestStream.CompleteAsync();
        Assert.True(await descriptor.ResponseStream.MoveNext(CancellationToken.None));
        Assert.Equal((int)StatusCode.NotFound, descriptor.ResponseStream.Current.ErrorResponse.ErrorCode);
    }

    [Trait("Layer", "Integration")]
    [Fact]
    public async Task GrpcDeclaration_AddResourceExposesRpcAndReflection_WithoutHttpRoute() {
        using var factory = Configure();
        using var http = factory.CreateClient(new() { BaseAddress = new("http://localhost") });
        using var channel = GrpcChannel.ForAddress(http.BaseAddress!, new() { HttpClient = http });
        var client = channel.CreateGrpcService<IResourceService<GrpcNote, GrpcNote, GrpcNote, GrpcNote>>(
            ClientFactory.Create(factory.Services.GetRequiredService<BinderConfiguration>()));

        var created = await client.CreateAsync(new() { Label = "gRPC declaration" });
        Assert.StartsWith("grpcNotes/consumer-", created.CanonicalName);
        var fetched = await client.GetAsync(new() { CanonicalName = created.CanonicalName });
        Assert.Equal(created.CanonicalName, fetched.CanonicalName);
        Assert.Equal("gRPC declaration", fetched.Label);
        using var list = await http.GetAsync("/v1/grpcNotes");
        Assert.Equal(HttpStatusCode.NotFound, list.StatusCode);
        using var get = await http.GetAsync($"/v1/{created.CanonicalName}");
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        using var post = await http.PostAsJsonAsync("/v1/grpcNotes", new { label = "excluded" });
        Assert.Equal(HttpStatusCode.NotFound, post.StatusCode);

        var reflection = new ServerReflection.ServerReflectionClient(channel);
        using var descriptor = reflection.ServerReflectionInfo();
        await descriptor.RequestStream.WriteAsync(new() { FileContainingSymbol = $"{typeof(GrpcNote).Namespace}.GrpcNoteService" });
        await descriptor.RequestStream.CompleteAsync();
        Assert.True(await descriptor.ResponseStream.MoveNext(CancellationToken.None));
        var files = descriptor.ResponseStream.Current.FileDescriptorResponse.FileDescriptorProto.Select(FileDescriptorProto.Parser.ParseFrom);
        var service = Assert.Single(files.SelectMany(file => file.Service), entry => entry.Name == "GrpcNoteService");
        Assert.Equal(new[] { "CreateGrpcNote", "DeleteGrpcNote", "GetGrpcNote", "ListGrpcNotes", "UpdateGrpcNote" },
            service.Method.Select(method => method.Name).OrderBy(name => name, StringComparer.Ordinal));
    }

    [Trait("Layer", "Integration")]
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitEmptyEndpoints_OverrideHttpDeclaration_WithBothTransports(bool configureEmpty) {
        using var factory = Configure(configureEmpty ? "ResourceDeclarationsConfigureEmpty" : "ResourceDeclarationsEmpty");
        using var http = factory.CreateClient(new() { BaseAddress = new("http://localhost") });
        using var channel = GrpcChannel.ForAddress(http.BaseAddress!, new() { HttpClient = http });
        var client = channel.CreateGrpcService<IResourceService<HttpNote, HttpNote, HttpNote, HttpNote>>(
            ClientFactory.Create(factory.Services.GetRequiredService<BinderConfiguration>()));

        using var created = await http.PostAsJsonAsync("/v1/httpNotes", new { label = "Explicit unrestricted" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        var name = body.GetProperty("name").GetString();
        var fetched = await client.GetAsync(new() { CanonicalName = name });
        Assert.Equal(name, fetched.CanonicalName);
        Assert.Equal("Explicit unrestricted", fetched.Label);
    }

    private WebApplicationFactory<Program> Configure(string? setting = null) {
        return _factory.WithWebHostBuilder(builder => {
            builder.UseSetting("ResourceDeclarations", "true");
            if (setting is not null) builder.UseSetting(setting, "true");
        });
    }
}
