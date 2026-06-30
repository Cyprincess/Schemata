using System.Threading.Tasks;
using ProtoBuf.Grpc.Client;
using Schemata.Resource.Grpc.Integration.Tests.Fixtures;
using Xunit;

namespace Schemata.Resource.Grpc.Integration.Tests;

[Collection("GrpcIntegration")]
[Trait("Category", "Integration")]
public class ResourceServiceBlockedShould
{
    private readonly WebAppFactory _factory;

    public ResourceServiceBlockedShould(WebAppFactory factory) { _factory = factory; }

    [Fact]
    public async Task List_DefaultAdvisors_ReturnsNonNullResult() {
        // With no blocking advisors and no data in DB, list returns an empty but non-null result.
        var (channel, clientFactory) = _factory.CreateGrpcChannelWithClient();
        var client = channel.CreateGrpcService<IResourceService<Student, Student, Student, Student>>(clientFactory);

        var result = await client.ListAsync(new());

        Assert.NotNull(result);
    }

    [Fact]
    public async Task Create_DefaultAdvisors_Succeeds() {
        // With no blocking advisors, create should succeed and return the created entity.
        var (channel, clientFactory) = _factory.CreateGrpcChannelWithClient();
        var client = channel.CreateGrpcService<IResourceService<Student, Student, Student, Student>>(clientFactory);

        var created = await client.CreateAsync(new() { FullName = "DefaultAdvisors" });

        Assert.NotNull(created);
        Assert.False(string.IsNullOrWhiteSpace(created.CanonicalName));
    }
}
