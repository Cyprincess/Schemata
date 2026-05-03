using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
    public async Task ListServices_IncludesStudentService() {
        var channel = _factory.CreateGrpcChannel();
        var client  = new ServerReflection.ServerReflectionClient(channel);

        using var call = client.ServerReflectionInfo();
        await call.RequestStream.WriteAsync(new() { ListServices = "" });
        await call.RequestStream.CompleteAsync();

        Assert.True(await call.ResponseStream.MoveNext(CancellationToken.None));
        var response = call.ResponseStream.Current;

        var services = response.ListServicesResponse.Service.Select(s => s.Name).ToList();

        Assert.Contains(services, s => s.Contains("Student"));
    }

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

        // CanonicalName must appear as "name", not "canonical_name"
        Assert.Contains("name", allFields);
        Assert.DoesNotContain("canonical_name", allFields);

        // EntityTag must appear as "etag", not "entity_tag"
        Assert.Contains("etag", allFields);
        Assert.DoesNotContain("entity_tag", allFields);

        // ListResult.Entities must be renamed to the entity plural ("students")
        Assert.Contains("students", allFields);
        Assert.DoesNotContain("entities", allFields);
    }
}
