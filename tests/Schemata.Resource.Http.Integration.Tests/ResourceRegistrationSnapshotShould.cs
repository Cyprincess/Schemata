using System;
using System.Collections.Generic;
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
using Schemata.Abstractions.Resource;
using Schemata.Resource.Grpc;
using Schemata.Resource.Http.Integration.Tests.Fixtures;
using Xunit;

namespace Schemata.Resource.Http.Integration.Tests;

[Trait("Category", "Integration")]
public class ResourceRegistrationSnapshotShould : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;

    public ResourceRegistrationSnapshotShould(WebAppFactory factory) { _factory = factory; }

    [Trait("Layer", "Integration")]
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RepeatedRegistration_SupplementsPolicyAndUnionsTransports_DespiteInputMutation(bool grpcFirst) {
        using var factory = Configure(grpcFirst);
        await VerifyWireAsync(factory);
    }

    [Trait("Layer", "Integration")]
    [Theory]
    [InlineData("tuple")]
    [InlineData("scheme")]
    [InlineData("page")]
    [InlineData("operations")]
    [InlineData("method")]
    public async Task ConflictingRegistration_RejectsBeforeChangingWorkingWire(string conflict) {
        using var factory = Configure(false, conflict);
        await VerifyWireAsync(factory);
    }

    [Trait("Layer", "Integration")]
    [Fact]
    public async Task ExplicitEmptyOperations_SuppressesStandardRoutesAndReflection_WhileKeepingCustomRoutes() {
        using var factory = Configure(false, empty: true);
        using var http = factory.CreateClient(new() { BaseAddress = new("http://localhost") });
        http.DefaultRequestHeaders.Add("X-Test-Auth", "valid");
        http.DefaultRequestHeaders.Add("X-Test-Scheme", "SnapshotScheme");
        using var channel = GrpcChannel.ForAddress(http.BaseAddress!, new() { HttpClient = http });
        var client = channel.CreateGrpcService<IResourceService<Student, Student, Student, Student>>(
            ClientFactory.Create(factory.Services.GetRequiredService<BinderConfiguration>()));

        using var absent = await http.GetAsync("/v1/students");
        Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);
        var error = await Assert.ThrowsAsync<RpcException>(async () => await client.ListAsync(new()));
        Assert.Equal(StatusCode.Unimplemented, error.StatusCode);
        var methods = await ReflectionMethodsAsync(channel);
        Assert.Equal(new[] { "InspectStudent", "PreviewStudent" }, methods);
        using var custom = await http.GetAsync("/v1/students/missing:inspect");
        Assert.Equal(HttpStatusCode.NotFound, custom.StatusCode);
        var body = await custom.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("NOT_FOUND", body.GetProperty("error").GetProperty("status").GetString());
    }

    private WebApplicationFactory<Program> Configure(bool grpcFirst, string? conflict = null, bool empty = false) {
        return _factory.WithWebHostBuilder(builder => {
            builder.UseSetting("ResourceSnapshot", "true");
            builder.UseSetting("ResourceSnapshotGrpcFirst", grpcFirst ? "true" : "false");
            if (conflict is not null) builder.UseSetting("ResourceSnapshotConflict", conflict);
            builder.UseSetting("ResourceSnapshotEmpty", empty ? "true" : "false");
        });
    }

    private static async Task VerifyWireAsync(WebApplicationFactory<Program> factory) {
        using var http = factory.CreateClient(new() { BaseAddress = new("http://localhost") });
        using var channel = GrpcChannel.ForAddress(http.BaseAddress!, new() { HttpClient = http });
        var client = channel.CreateGrpcService<IResourceService<Student, Student, Student, Student>>(
            ClientFactory.Create(factory.Services.GetRequiredService<BinderConfiguration>()));
        using var challenge = await http.GetAsync("/v1/students");
        Assert.Equal(HttpStatusCode.Unauthorized, challenge.StatusCode);
        var grpcChallenge = await Assert.ThrowsAsync<RpcException>(async () => await client.ListAsync(new()));
        Assert.Equal(StatusCode.Unauthenticated, grpcChallenge.StatusCode);

        http.DefaultRequestHeaders.Add("X-Test-Auth", "valid");
        http.DefaultRequestHeaders.Add("X-Test-Scheme", "SnapshotScheme");
        var names = new List<string>();
        for (var i = 0; i < 5; i++) {
            using var created = await http.PostAsJsonAsync("/v1/students", new { full_name = $"Snapshot{i}" });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var body = await created.Content.ReadFromJsonAsync<JsonElement>();
            names.Add(body.GetProperty("name").GetString()!);
        }

        var input = factory.Services.GetRequiredService<ResourceAttribute>();
        input.AuthenticationScheme = "ChangedAfterSeal";
        input.DefaultPageSize = 1;
        input.MaxPageSize = 1;
        input.Endpoints!.Clear();
        input.Methods!.Clear();

        using var listed = await http.GetAsync("/v1/students?order_by=uid asc");
        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        var httpPage = await listed.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, httpPage.GetProperty("students").GetArrayLength());
        Assert.False(httpPage.TryGetProperty("total_size", out _));
        var httpNames = httpPage.GetProperty("students").EnumerateArray()
                                .Select(row => row.GetProperty("name").GetString()).ToArray();
        var grpcPage = await client.ListAsync(new() { OrderBy = "uid asc" });
        Assert.Equal(httpNames, grpcPage.Entities!.Select(row => row.CanonicalName).ToArray());
        Assert.Null(grpcPage.TotalSize);
        Assert.False(string.IsNullOrEmpty(grpcPage.NextPageToken));

        using var capped = await http.GetAsync("/v1/students?page_size=100&order_by=uid asc");
        Assert.Equal(HttpStatusCode.OK, capped.StatusCode);
        var cappedBody = await capped.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(3, cappedBody.GetProperty("students").GetArrayLength());
        var grpcCapped = await client.ListAsync(new() { PageSize = 100, OrderBy = "uid asc" });
        Assert.Equal(cappedBody.GetProperty("students").EnumerateArray().Select(row => row.GetProperty("name").GetString()),
            grpcCapped.Entities!.Select(row => row.CanonicalName));

        using var inspected = await http.GetAsync($"/v1/{names[0]}:inspect");
        Assert.Equal(HttpStatusCode.OK, inspected.StatusCode);
        var inspectedBody = await inspected.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(names[0], inspectedBody.GetProperty("name").GetString());
        using var wrongMethod = await http.PostAsJsonAsync($"/v1/{names[0]}:inspect", new { });
        Assert.Equal(HttpStatusCode.MethodNotAllowed, wrongMethod.StatusCode);
        using var disabled = await http.DeleteAsync($"/v1/{names[0]}");
        Assert.Equal(HttpStatusCode.MethodNotAllowed, disabled.StatusCode);
        var get = await client.GetAsync(new() { CanonicalName = names[0] });
        Assert.Equal("Snapshot0", get.FullName);

        Assert.Equal(new[] { "CreateStudent", "GetStudent", "InspectStudent", "ListStudents", "PreviewStudent" },
            await ReflectionMethodsAsync(channel));
    }

    public static async Task<string[]> ReflectionMethodsAsync(GrpcChannel channel) {
        var reflection = new ServerReflection.ServerReflectionClient(channel);
        using var call = reflection.ServerReflectionInfo();
        await call.RequestStream.WriteAsync(new() { FileContainingSymbol = $"{typeof(Student).Namespace}.StudentService" });
        await call.RequestStream.CompleteAsync();
        Assert.True(await call.ResponseStream.MoveNext(CancellationToken.None));
        var files = call.ResponseStream.Current.FileDescriptorResponse.FileDescriptorProto.Select(FileDescriptorProto.Parser.ParseFrom);
        return Assert.Single(files.SelectMany(file => file.Service), service => service.Name == "StudentService")
                     .Method.Select(method => method.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray();
    }
}
