using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProtoBuf.Grpc.Client;
using Schemata.Resource.Grpc.Integration.Tests.Fixtures;
using Xunit;

namespace Schemata.Resource.Grpc.Integration.Tests;

[Collection("GrpcIntegration")]
[Trait("Layer", "Integration")]
[Trait("Category", "Integration")]
public class ResourceDryRunShould(WebAppFactory factory)
{
    [Fact]
    public async Task Create_ValidateOnly_ReturnsDefaultDetail_AndPersistsNothing() {
        var (channel, clientFactory) = factory.CreateGrpcChannelWithClient();
        using var ownedChannel = channel;
        var client = channel.CreateGrpcService<IResourceService<Student, Student, Student, Student>>(clientFactory);
        var fullName = $"GrpcDryRun{Guid.NewGuid():N}";

        var result = await client.CreateAsync(new() { FullName = fullName, ValidateOnly = true });

        Assert.NotNull(result);
        Assert.Null(result.Name);
        Assert.Null(result.CanonicalName);
        Assert.Null(result.FullName);
        await using var db = await factory.Services.GetRequiredService<IDbContextFactory<TestDbContext>>()
                                          .CreateDbContextAsync();
        Assert.False(await db.Students.AnyAsync(row => row.FullName == fullName));
    }
}
