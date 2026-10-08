using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf.Reflection;
using Grpc.Core;
using Grpc.Net.Client;
using Grpc.Reflection.V1Alpha;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using ProtoBuf.Grpc.Client;
using ProtoBuf.Grpc.Configuration;
using Schemata.Abstractions.Resource;
using Schemata.Resource.Grpc.Integration.Tests.Fixtures;
using Xunit;

namespace Schemata.Resource.Grpc.Integration.Tests;

[Collection("GrpcIntegration")]
[Trait("Category", "Integration")]
public class ResourceRegistrationSnapshotShould
{
    private readonly WebAppFactory _factory;

    public ResourceRegistrationSnapshotShould(WebAppFactory factory) { _factory = factory; }

    [Trait("Layer", "Integration")]
    [Fact]
    public async Task LaterPolicySupplement_PreservesPagingAuthenticationAndReflection_AfterInputMutation() {
        using var factory = _factory.WithWebHostBuilder(builder => builder.UseSetting("ResourceSnapshot", "true"));
        using var http = factory.CreateClient(new() { BaseAddress = new("http://localhost") });
        using var channel = GrpcChannel.ForAddress(http.BaseAddress!, new() { HttpClient = http });
        var client = channel.CreateGrpcService<IResourceService<Student, Student, Student, Student>>(
            ClientFactory.Create(factory.Services.GetRequiredService<BinderConfiguration>()));
        var challenge = await Assert.ThrowsAsync<RpcException>(async () => await client.ListAsync(new()));
        Assert.Equal(StatusCode.Unauthenticated, challenge.StatusCode);
        http.DefaultRequestHeaders.Add("X-Test-Auth", "valid");
        http.DefaultRequestHeaders.Add("X-Test-Scheme", "SnapshotScheme");
        var created = new List<Student>();
        for (var i = 0; i < 5; i++) created.Add(await client.CreateAsync(new() { FullName = $"Snapshot{i}" }));

        var input = factory.Services.GetRequiredService<ResourceAttribute>();
        input.Endpoints!.Clear();
        input.AuthenticationScheme = "ChangedAfterSeal";
        input.DefaultPageSize = 1;
        input.MaxPageSize = 1;
        input.Operations = [];
        var expected = created.OrderBy(student => student.FullName, StringComparer.Ordinal).Select(student => student.CanonicalName).ToArray();
        var first = await client.ListAsync(new() { OrderBy = "full_name asc" });
        Assert.Equal(expected.Take(2), first.Entities!.Select(student => student.CanonicalName));
        Assert.Null(first.TotalSize);
        Assert.False(string.IsNullOrEmpty(first.NextPageToken));
        var capped = await client.ListAsync(new() { PageSize = 100, OrderBy = "full_name asc" });
        Assert.Equal(expected.Take(3), capped.Entities!.Select(student => student.CanonicalName));
        var fetched = await client.GetAsync(new() { CanonicalName = created[0].CanonicalName });
        Assert.Equal("Snapshot0", fetched.FullName);
        var removed = await Assert.ThrowsAsync<RpcException>(async () => await client.DeleteAsync(new() {
            CanonicalName = created[0].CanonicalName,
        }));
        Assert.Equal(StatusCode.Unimplemented, removed.StatusCode);
        Assert.Equal(new[] { "CreateStudent", "GetStudent", "ListStudents" }, await ReflectionMethodsAsync(channel));
    }

    [Trait("Layer", "Integration")]
    [Fact]
    public async Task ExplicitEmptyOperations_SuppressesRuntimeRpcAndReflectedMethods() {
        using var factory = _factory.WithWebHostBuilder(builder => {
            builder.UseSetting("ResourceSnapshot", "true");
            builder.UseSetting("ResourceSnapshotEmpty", "true");
        });
        using var http = factory.CreateClient(new() { BaseAddress = new("http://localhost") });
        http.DefaultRequestHeaders.Add("X-Test-Auth", "valid");
        http.DefaultRequestHeaders.Add("X-Test-Scheme", "SnapshotScheme");
        using var channel = GrpcChannel.ForAddress(http.BaseAddress!, new() { HttpClient = http });
        var client = channel.CreateGrpcService<IResourceService<Student, Student, Student, Student>>(
            ClientFactory.Create(factory.Services.GetRequiredService<BinderConfiguration>()));
        var error = await Assert.ThrowsAsync<RpcException>(async () => await client.ListAsync(new()));
        Assert.Equal(StatusCode.Unimplemented, error.StatusCode);
        var response = await ReflectionResponseAsync(channel);
        Assert.Equal(ServerReflectionResponse.MessageResponseOneofCase.ErrorResponse, response.MessageResponseCase);
        Assert.Equal((int)StatusCode.NotFound, response.ErrorResponse.ErrorCode);
    }

    private static async Task<string[]> ReflectionMethodsAsync(GrpcChannel channel) {
        var response = await ReflectionResponseAsync(channel);
        Assert.Equal(ServerReflectionResponse.MessageResponseOneofCase.FileDescriptorResponse, response.MessageResponseCase);
        var files = response.FileDescriptorResponse.FileDescriptorProto.Select(FileDescriptorProto.Parser.ParseFrom);
        return Assert.Single(files.SelectMany(file => file.Service), service => service.Name == "StudentService")
                     .Method.Select(method => method.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray();
    }

    private static async Task<ServerReflectionResponse> ReflectionResponseAsync(GrpcChannel channel) {
        var reflection = new ServerReflection.ServerReflectionClient(channel);
        using var call = reflection.ServerReflectionInfo();
        await call.RequestStream.WriteAsync(new() { FileContainingSymbol = $"{typeof(Student).Namespace}.StudentService" });
        await call.RequestStream.CompleteAsync();
        Assert.True(await call.ResponseStream.MoveNext(CancellationToken.None));
        return call.ResponseStream.Current;
    }
}
