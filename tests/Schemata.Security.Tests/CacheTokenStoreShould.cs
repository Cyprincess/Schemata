using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Schemata.Caching.Skeleton;
using Schemata.Security.Foundation.Stores;
using Schemata.Security.Skeleton.Entities;
using Xunit;

namespace Schemata.Security.Tests;

public class CacheTokenStoreShould
{
    private static readonly DateTime Now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Same_Slot_Twice_Returns_The_Stored_Value() {
        var (cache, _, _) = Cache();
        var store = new CacheTokenStore(cache.Object);

        var first  = await store.GetOrCreateAsync(null, "dpop", "as:client-1", "candidate", TimeSpan.FromMinutes(5));
        var second = await store.GetOrCreateAsync(null, "dpop", "as:client-1", "candidate", TimeSpan.FromMinutes(5));

        Assert.Equal("candidate", first.Value);
        Assert.Equal(first.Value, second.Value);
    }

    [Fact]
    public async Task GetOrCreate_TryAdd_Win_Returns_The_Candidate_With_The_Requested_Ttl() {
        var (cache, _, entries) = Cache();
        var store = new CacheTokenStore(cache.Object, Clock().Object);

        var row = await store.GetOrCreateAsync("users/u-1", "dpop", "as:client-1", "candidate", TimeSpan.FromMinutes(5));

        Assert.Equal("candidate",          row.Value);
        Assert.Equal("users/u-1",          row.Parent);
        Assert.Equal("dpop",               row.Provider);
        Assert.Equal("as:client-1",        row.Name);
        Assert.Equal(Now.AddMinutes(5),    row.ExpireTime);
        var options = Assert.Single(entries);
        Assert.Equal(TimeSpan.FromMinutes(5), options.AbsoluteExpirationRelativeToNow);
    }

    [Fact]
    public async Task GetOrCreate_TryAdd_Lose_Returns_The_Winner_Value() {
        var (cache, store, _) = Cache();
        cache.Setup(
                 value => value.TryAddAsync(
                     It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CacheEntryOptions>(),
                     It.IsAny<CancellationToken>()))
             .Callback((string key, byte[] _, CacheEntryOptions _, CancellationToken _) =>
                 store[key] = Encoding.UTF8.GetBytes("winner"))
             .ReturnsAsync(false);
        var sut = new CacheTokenStore(cache.Object);

        var row = await sut.GetOrCreateAsync(null, "dpop", "as:client-1", "candidate", TimeSpan.FromMinutes(5));

        Assert.Equal("winner", row.Value);
    }

    [Fact]
    public async Task GetOrCreate_TryAdd_Lose_Returns_Own_Candidate_When_The_Winner_Expired() {
        var (cache, _, _) = Cache();
        cache.Setup(
                 value => value.TryAddAsync(
                     It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CacheEntryOptions>(),
                     It.IsAny<CancellationToken>()))
             .ReturnsAsync(false);
        var sut = new CacheTokenStore(cache.Object);

        var row = await sut.GetOrCreateAsync(null, "dpop", "as:client-1", "candidate", TimeSpan.FromMinutes(5));

        Assert.Equal("candidate", row.Value);
    }

    [Fact]
    public async Task GetOrCreate_Mints_A_Value_When_Given_None() {
        var (cache, _, _) = Cache();
        var sut = new CacheTokenStore(cache.Object);

        var row = await sut.GetOrCreateAsync(null, "dpop", "as:client-1", null, TimeSpan.FromMinutes(5));

        Assert.Matches("^[0-9A-F]{64}$", row.Value);
    }

    [Fact]
    public async Task Get_Set_And_Remove_Round_Trip_The_Slot_Value() {
        var (cache, _, _) = Cache();
        var sut = new CacheTokenStore(cache.Object);

        await sut.SetAsync(null, "device", "rate:k-1", "3", TimeSpan.FromSeconds(10));
        var stored = await sut.GetAsync(null, "device", "rate:k-1");

        Assert.Equal("3", stored!.Value);

        await sut.RemoveAsync(null, "device", "rate:k-1");
        Assert.Null(await sut.GetAsync(null, "device", "rate:k-1"));
    }

    [Fact]
    public async Task Distinct_Slots_Are_Addressed_Independently() {
        var (cache, _, _) = Cache();
        var sut = new CacheTokenStore(cache.Object);

        await sut.SetAsync(null, "dpop", "as:client-1", "as-value", TimeSpan.FromMinutes(5));
        await sut.SetAsync(null, "dpop", "rs:client-1", "rs-value", TimeSpan.FromMinutes(5));

        Assert.Equal("as-value", (await sut.GetAsync(null, "dpop", "as:client-1"))!.Value);
        Assert.Equal("rs-value", (await sut.GetAsync(null, "dpop", "rs:client-1"))!.Value);
    }

    [Fact]
    public async Task State_Machine_Operations_Are_Not_Supported() {
        var (cache, _, _) = Cache();
        var sut = new CacheTokenStore(cache.Object);
        var token = new SchemataToken { Name = "nonce" };

        await Assert.ThrowsAsync<NotSupportedException>(() => sut.TryRedeemAsync(token));
        await Assert.ThrowsAsync<NotSupportedException>(() => sut.RevokeAsync(token));
        await Assert.ThrowsAsync<NotSupportedException>(() => sut.RevokeByAuthorizationAsync("auth-1"));
        await Assert.ThrowsAsync<NotSupportedException>(() => sut.RevokeBySessionAsync("sid-1"));
        await Assert.ThrowsAsync<NotSupportedException>(() => sut.PruneAsync());
    }

    [Fact]
    public async Task Queries_And_Row_Crud_Are_Not_Supported() {
        var (cache, _, _) = Cache();
        var sut = new CacheTokenStore(cache.Object);

        await Assert.ThrowsAsync<NotSupportedException>(() => sut.FindByReferenceIdAsync("ref-1"));
        await Assert.ThrowsAsync<NotSupportedException>(() => sut.FindByNameAsync("nonce"));
        Assert.Throws<NotSupportedException>(() => sut.ListBySessionAsync("sid-1"));
        Assert.Throws<NotSupportedException>(() => sut.ListByParentAsync("users/u-1"));
        await Assert.ThrowsAsync<NotSupportedException>(() => sut.CreateAsync(new()));
        await Assert.ThrowsAsync<NotSupportedException>(() => sut.UpdateAsync(new()));
    }

    private static (
        Mock<ICacheProvider>       Cache,
        Dictionary<string, byte[]> Store,
        List<CacheEntryOptions>    Entries
    ) Cache() {
        var store   = new Dictionary<string, byte[]>();
        var entries = new List<CacheEntryOptions>();
        var cache   = new Mock<ICacheProvider>();
        cache.Setup(value => value.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((string key, CancellationToken _) =>
                 store.TryGetValue(key, out var bytes) ? bytes : null);
        cache.Setup(value => value.SetAsync(
                         It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CacheEntryOptions>(),
                         It.IsAny<CancellationToken>()))
             .Callback((string key, byte[] value, CacheEntryOptions _, CancellationToken _) =>
                 store[key] = value)
             .Returns(Task.CompletedTask);
        cache.Setup(value => value.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .Callback((string key, CancellationToken _) => store.Remove(key))
             .Returns(Task.CompletedTask);
        cache.Setup(
                 value => value.TryAddAsync(
                     It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CacheEntryOptions>(),
                     It.IsAny<CancellationToken>()))
             .Returns((string key, byte[] value, CacheEntryOptions options, CancellationToken _) => {
                 entries.Add(options);

                 return Task.FromResult(store.TryAdd(key, value));
             });
        return (cache, store, entries);
    }

    private static Mock<TimeProvider> Clock() {
        var time = new Mock<TimeProvider>();
        time.Setup(t => t.GetUtcNow()).Returns(new DateTimeOffset(Now, TimeSpan.Zero));

        return time;
    }
}
