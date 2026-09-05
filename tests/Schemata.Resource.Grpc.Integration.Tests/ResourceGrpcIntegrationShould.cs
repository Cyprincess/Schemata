using System;
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
    public async Task List_Returns_Populated_Envelope() {
        var (channel, clientFactory) = _factory.CreateGrpcChannelWithClient();
        var client = channel.CreateGrpcService<IResourceService<Student, Student, Student, Student>>(clientFactory);

        var created = await client.CreateAsync(new() { FullName = "GrpcListStudent" });

        var result = await client.ListAsync(new());

        Assert.NotNull(result);
        Assert.NotNull(result.Entities);
        var listed = Assert.Single(result.Entities, s => s.CanonicalName == created.CanonicalName);
        Assert.Equal("GrpcListStudent", listed.FullName);
        Assert.NotNull(result.TotalSize);
        Assert.True(result.TotalSize >= result.Entities.Count);
        Assert.True(string.IsNullOrEmpty(result.NextPageToken));
    }

    [Fact]
    public async Task Create_ThenGet_ReturnsEntity() {
        var (channel, clientFactory) = _factory.CreateGrpcChannelWithClient();
        var client = channel.CreateGrpcService<IResourceService<Student, Student, Student, Student>>(clientFactory);

        var created = await client.CreateAsync(new() { FullName = "GrpcStudent" });
        Assert.NotNull(created);
        Assert.Equal("GrpcStudent", created.FullName);
        Assert.NotEqual(Guid.Empty, created.Uid);
        Assert.StartsWith("students/", created.CanonicalName);

        var fetched = await client.GetAsync(new() { CanonicalName = created.CanonicalName });
        Assert.Equal(created.FullName, fetched.FullName);
        Assert.Equal(created.Uid, fetched.Uid);
        Assert.Equal(created.CanonicalName, fetched.CanonicalName);
    }

    [Fact]
    public async Task Delete_MissingStudent_WithAllowMissing_Succeeds() {
        var (channel, clientFactory) = _factory.CreateGrpcChannelWithClient();
        var client = channel.CreateGrpcService<IResourceService<Student, Student, Student, Student>>(clientFactory);

        // A hard delete resolves to an empty message per AIP-135; allow_missing
        // suppresses NotFound for a name that never existed. Proto has no null
        // message, so the empty response carries no resource state.
        var deleted = await client.DeleteAsync(new() {
            CanonicalName = "students/does-not-exist",
            AllowMissing  = true,
        });

        Assert.True(string.IsNullOrEmpty(deleted?.CanonicalName));
    }
}
