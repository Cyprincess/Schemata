using System.Threading.Tasks;
using ProtoBuf.Grpc.Client;
using Schemata.Resource.Grpc.Integration.Tests.Fixtures;
using Xunit;

namespace Schemata.Resource.Grpc.Integration.Tests;

[Collection("GrpcIntegration")]
[Trait("Category", "Integration")]
public class ResourceGrpcIntegrationShould
{
    private readonly WebAppFactory _factory;

    public ResourceGrpcIntegrationShould(WebAppFactory factory) { _factory = factory; }

    [Fact]
    public async Task List_ReturnsResult() {
        var (channel, clientFactory) = _factory.CreateGrpcChannelWithClient();
        var client = channel.CreateGrpcService<IResourceService<Student, Student, Student, Student>>(clientFactory);

        var result = await client.ListAsync(new());
        Assert.NotNull(result);
    }

    [Fact]
    public async Task Create_ThenGet_ReturnsEntity() {
        var (channel, clientFactory) = _factory.CreateGrpcChannelWithClient();
        var client = channel.CreateGrpcService<IResourceService<Student, Student, Student, Student>>(clientFactory);

        var created = await client.CreateAsync(new() { FullName = "GrpcStudent" });
        Assert.NotNull(created);
        Assert.False(string.IsNullOrWhiteSpace(created.CanonicalName), "CanonicalName should be auto-set");

        var fetched = await client.GetAsync(new() { CanonicalName = created.CanonicalName });
        Assert.Equal("GrpcStudent", fetched.FullName);
    }
}
