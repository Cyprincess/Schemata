using System.Threading.Tasks;
using Grpc.Core;
using ProtoBuf.Grpc.Client;
using Schemata.Resource.Grpc.Integration.Tests.Fixtures;
using Xunit;

namespace Schemata.Resource.Grpc.Integration.Tests;

[Collection("GrpcIntegration")]
[Trait("Category", "Integration")]
public class ResourceGrpcErrorIntegrationShould
{
    private readonly WebAppFactory _factory;

    public ResourceGrpcErrorIntegrationShould(WebAppFactory factory) { _factory = factory; }

    [Fact]
    public async Task Get_NonExistentName_ThrowsNotFound() {
        var (channel, clientFactory) = _factory.CreateGrpcChannelWithClient();
        var client = channel.CreateGrpcService<IResourceService<Student, Student, Student, Student>>(clientFactory);

        var rpc = await Assert.ThrowsAsync<RpcException>(() => client
                                                              .GetAsync(
                                                                   new() {
                                                                       CanonicalName = "students/does-not-exist-99999",
                                                                   }
                                                               )
                                                              .AsTask()
        );

        Assert.Equal(StatusCode.NotFound, rpc.StatusCode);
    }

    [Fact]
    public async Task Update_WrongETag_ThrowsAborted() {
        var (channel, clientFactory) = _factory.CreateGrpcChannelWithClient();
        var client = channel.CreateGrpcService<IResourceService<Student, Student, Student, Student>>(clientFactory);

        var created = await client.CreateAsync(new() { FullName = "ETagCanary" });
        Assert.NotNull(created);
        Assert.False(string.IsNullOrWhiteSpace(created.CanonicalName), "CanonicalName should be auto-set");

        // Send wrong ETag — AdviceUpdateFreshness will throw ConcurrencyException → Aborted
        var update = new Student {
            FullName = "Updated", EntityTag = "W/\"wrong-etag-value\"", CanonicalName = created.CanonicalName,
        };

        var rpc = await Assert.ThrowsAsync<RpcException>(() => client.UpdateAsync(update).AsTask());

        Assert.Equal(StatusCode.Aborted, rpc.StatusCode);
    }
}
