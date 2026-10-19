using Grpc.Core;
using ProtoBuf;
using ProtoBuf.Meta;
using Schemata.Abstractions.Resource;
using Schemata.Transport.Grpc;
using Schemata.Transport.Grpc.Proto;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Grpc.Reflection.V1Alpha;
using Schemata.Resource.Grpc.Integration.Tests.Fixtures;
using Xunit;

namespace Schemata.Resource.Grpc.Integration.Tests;

[Collection("GrpcIntegration")]
[Trait("Category", "Integration")]
public class ResourceGrpcReflectionShould
{
    private readonly WebAppFactory _factory;

    public ResourceGrpcReflectionShould(WebAppFactory factory) { _factory = factory; }


    [Fact]
    public async Task FileDescriptor_HasCorrectFieldRenaming() {
        var channel = _factory.CreateGrpcChannel();
        var client  = new ServerReflection.ServerReflectionClient(channel);

        using var call = client.ServerReflectionInfo();

        await call.RequestStream.WriteAsync(new() {
            FileContainingSymbol = "Schemata.Resource.Grpc.Integration.Tests.Fixtures.StudentService",
        });
        await call.RequestStream.CompleteAsync();

        Assert.True(await call.ResponseStream.MoveNext(CancellationToken.None));
        var response = call.ResponseStream.Current;
        Assert.NotNull(response.FileDescriptorResponse);

        var files = response.FileDescriptorResponse.FileDescriptorProto.Select(FileDescriptorProto.Parser.ParseFrom)
                            .ToList();

        var allFields = files.Where(f => !f.Name.StartsWith("google/"))
                             .SelectMany(f => f.MessageType)
                             .SelectMany(m => m.Field)
                             .Select(f => f.Name)
                             .ToHashSet();

        Assert.Contains("name", allFields);
        Assert.DoesNotContain("canonical_name", allFields);

        Assert.Contains("etag", allFields);
        Assert.DoesNotContain("entity_tag", allFields);

        Assert.Contains("students", allFields);
        Assert.DoesNotContain("entities", allFields);
        var service = files.SelectMany(file => file.Service).Single(service => service.Name == "StudentService");
        var get = service.Method.Single(method => method.Name == "GetStudent");
        var package = files.Single(file => file.Service.Contains(service)).Package;
        var model = RuntimeTypeModel.Create();
        model.DefaultCompatibilityLevel = CompatibilityLevel.Level300;
        SchemataProtoModelConfigurator.ConfigureType(model, typeof(GetRequest));
        SchemataProtoModelConfigurator.ConfigureType(model, typeof(Student));
        var method = new Method<GetRequest, Student>(MethodType.Unary, $"{package}.{service.Name}", get.Name,
            GrpcMarshallers.Create<GetRequest>(model), GrpcMarshallers.Create<Student>(model));
        using var missing = channel.CreateCallInvoker().AsyncUnaryCall(method, null, new(), new() { CanonicalName = "students/missing-reflection" });
        var failure = await Assert.ThrowsAsync<RpcException>(async () => await missing.ResponseAsync);
        Assert.Equal(StatusCode.NotFound, failure.StatusCode);
    }

    [Fact]
    public async Task FileDescriptor_Uses_Stable_BuiltIn_Request_Names_And_Omits_Principal() {
        var channel = _factory.CreateGrpcChannel();
        var client  = new ServerReflection.ServerReflectionClient(channel);

        using var call = client.ServerReflectionInfo();
        await call.RequestStream.WriteAsync(new() {
            FileContainingSymbol = "Schemata.Resource.Grpc.Integration.Tests.Fixtures.TrashService",
        });
        await call.RequestStream.CompleteAsync();

        Assert.True(await call.ResponseStream.MoveNext(CancellationToken.None));
        var files = call.ResponseStream.Current.FileDescriptorResponse.FileDescriptorProto
                        .Select(FileDescriptorProto.Parser.ParseFrom);
        var messages = files.Where(file => !file.Name.StartsWith("google/"))
                            .SelectMany(file => file.MessageType)
                            .ToList();

        foreach (var name in new[] {
                     "UndeleteResourceRequestOfTrashAndTrash",
                     "ExpungeResourceRequestOfTrash",
                     "PurgeResourceRequestOfTrash",
                 }) {
            var message = Assert.Single(messages, descriptor => descriptor.Name == name);
            Assert.DoesNotContain(message.Field, field => field.Name == "principal");
        }
    }

    [Fact]
    public async Task FileDescriptor_Annotates_RequestId_With_Uuid4_Format() {
        var channel = _factory.CreateGrpcChannel();
        var client  = new ServerReflection.ServerReflectionClient(channel);

        using var call = client.ServerReflectionInfo();
        await call.RequestStream.WriteAsync(new() {
            FileContainingSymbol = "Schemata.Resource.Grpc.Integration.Tests.Fixtures.IdempotentOrderService",
        });
        await call.RequestStream.CompleteAsync();

        Assert.True(await call.ResponseStream.MoveNext(CancellationToken.None));
        var registry = new ExtensionRegistry { Google.Api.FieldInfoExtensions.FieldInfo };
        var files = call.ResponseStream.Current.FileDescriptorResponse.FileDescriptorProto
                        .Select(bytes => FileDescriptorProto.Parser.WithExtensionRegistry(registry).ParseFrom(bytes))
                        .ToList();

        var message = files.Where(file => !file.Name.StartsWith("google/"))
                           .SelectMany(file => file.MessageType)
                           .Single(descriptor => descriptor.Name == "IdempotentOrderRequest");
        var field = Assert.Single(message.Field, field => field.Name == "request_id");
        var info = field.Options.GetExtension(Google.Api.FieldInfoExtensions.FieldInfo);
        Assert.NotNull(info);
        Assert.Equal(Google.Api.FieldInfo.Types.Format.Uuid4, info.Format);

        var declaring = files.Single(file => file.MessageType.Contains(message));
        Assert.Contains("google/api/field_info.proto", declaring.Dependency);
        Assert.Contains(files, file => file.Name == "google/api/field_info.proto");
    }

}
