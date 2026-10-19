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
public class ResourceGrpcNoWriteShould(WebAppFactory factory)
{
    [Fact]
    public async Task Create_NoWrite_ReturnsEmptyResource_AndPersistsNothing() {
        var (channel, clientFactory) = factory.CreateGrpcChannelWithClient();
        using var ownedChannel = channel;
        var client = channel.CreateGrpcService<IResourceService<Trash, Trash, Trash, Trash>>(clientFactory);
        var dbFactory = factory.Services.GetRequiredService<IDbContextFactory<TestDbContext>>();

        var result = await client.CreateAsync(new() { FullName = "NoWrite" });

        AssertEmpty(result);
        await using var after = await dbFactory.CreateDbContextAsync();
        Assert.False(await after.Trashes.AnyAsync(row => row.FullName == "NoWrite"));
    }

    [Fact]
    public async Task Update_NoWrite_ReturnsEmptyResource_AndPreservesStoredValues() {
        var (channel, clientFactory) = factory.CreateGrpcChannelWithClient();
        using var ownedChannel = channel;
        var client = channel.CreateGrpcService<IResourceService<Trash, Trash, Trash, Trash>>(clientFactory);
        var created = await client.CreateAsync(new() { FullName = "OriginalNoWrite" });
        Assert.NotNull(created);
        await using var before = await factory.Services.GetRequiredService<IDbContextFactory<TestDbContext>>().CreateDbContextAsync();
        var original = await before.Trashes.AsNoTracking().SingleAsync(row => row.Uid == created.Uid);

        var result = await client.UpdateAsync(new() {
            CanonicalName = created.CanonicalName, FullName = "NoWrite", UpdateMask = "full_name",
        });

        AssertEmpty(result);
        await using var db = await factory.Services.GetRequiredService<IDbContextFactory<TestDbContext>>().CreateDbContextAsync();
        var stored = await db.Trashes.AsNoTracking().SingleAsync(row => row.Uid == created.Uid);
        Assert.Equal("OriginalNoWrite", stored.FullName);
        Assert.Equal(created.CanonicalName, stored.CanonicalName);
        Assert.Equal(original.Timestamp, stored.Timestamp);
        Assert.Null(stored.DeleteTime);
        db.Trashes.Remove(stored);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Delete_MissingSoftResource_WithAllowMissing_ReturnsEmptyResource_AndPersistsNothing() {
        var (channel, clientFactory) = factory.CreateGrpcChannelWithClient();
        using var ownedChannel = channel;
        var client = channel.CreateGrpcService<IResourceService<Trash, Trash, Trash, Trash>>(clientFactory);
        var dbFactory = factory.Services.GetRequiredService<IDbContextFactory<TestDbContext>>();

        var result = await client.DeleteAsync(new() { CanonicalName = "trashes/missing-no-write", AllowMissing = true });

        AssertEmpty(result);
        await using var after = await dbFactory.CreateDbContextAsync();
        Assert.False(await after.Trashes.AnyAsync(row => row.CanonicalName == "trashes/missing-no-write"));
    }

    [Fact]
    public async Task Delete_Tombstone_WithAllowMissing_ReturnsEmptyResource_AndPreservesDeletion() {
        var (channel, clientFactory) = factory.CreateGrpcChannelWithClient();
        using var ownedChannel = channel;
        var client = channel.CreateGrpcService<IResourceService<Trash, Trash, Trash, Trash>>(clientFactory);
        var created = await client.CreateAsync(new() { FullName = "TombstoneNoWrite" });
        Assert.NotNull(created);
        var deleted = await client.DeleteAsync(new() { CanonicalName = created.CanonicalName });
        Assert.NotNull(deleted);
        Assert.NotNull(deleted.DeleteTime);

        await using var before = await factory.Services.GetRequiredService<IDbContextFactory<TestDbContext>>().CreateDbContextAsync();
        var tombstone = await before.Trashes.AsNoTracking().SingleAsync(row => row.Uid == created.Uid);
        var result = await client.DeleteAsync(new() { CanonicalName = created.CanonicalName, AllowMissing = true });

        AssertEmpty(result);
        await using var db = await factory.Services.GetRequiredService<IDbContextFactory<TestDbContext>>().CreateDbContextAsync();
        var stored = await db.Trashes.AsNoTracking().SingleAsync(row => row.Uid == created.Uid);
        Assert.Equal("TombstoneNoWrite", stored.FullName);
        Assert.Equal(tombstone.DeleteTime, stored.DeleteTime);
        Assert.Equal(tombstone.PurgeTime, stored.PurgeTime);
        Assert.Equal(tombstone.Timestamp, stored.Timestamp);
        db.Trashes.Remove(stored);
        await db.SaveChangesAsync();
    }

    private static void AssertEmpty(Trash? result) {
        Assert.NotNull(result);
        Assert.Null(result.CanonicalName);
        Assert.Null(result.Name);
        Assert.Null(result.FullName);
    }
}
