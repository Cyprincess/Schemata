using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Caching.Skeleton;
using StackExchange.Redis;
using Xunit;

namespace Schemata.Caching.Redis.Tests;

[Trait("Layer", "Unit")]
public class RedisCacheProviderShould
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Delete_RemovesEntryAndMetadata_LeavingOtherEntries(bool clearCollection) {
        var keys = new HashSet<RedisKey> { "{entry}", "{entry}:__meta__", "other" };
        var database = new Mock<IDatabase>(MockBehavior.Strict);
        database.Setup(db => db.KeyDeleteAsync(It.IsAny<RedisKey[]>(), CommandFlags.None))
                .ReturnsAsync((RedisKey[] deletedKeys, CommandFlags _) => {
                     long removed = 0;
                     foreach (var key in deletedKeys) {
                         if (keys.Remove(key)) {
                             removed++;
                         }
                     }

                     return removed;
                 });
        var provider = CreateProvider(database);

        var deletion = clearCollection
            ? provider.CollectionClearAsync("{entry}")
            : provider.RemoveAsync("{entry}");
        await deletion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.DoesNotContain((RedisKey)"{entry}", keys);
        Assert.DoesNotContain((RedisKey)"{entry}:__meta__", keys);
        Assert.Equal((RedisKey)"other", Assert.Single(keys));
    }

    [Fact]
    public async Task RemoveAsync_PendingDeleteFailure_PropagatesToCaller() {
        var pending = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
        var database = new Mock<IDatabase>(MockBehavior.Strict);
        database.Setup(db => db.KeyDeleteAsync(It.IsAny<RedisKey[]>(), CommandFlags.None))
                .Returns(pending.Task);
        var provider = CreateProvider(database);
        var failure = new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Redis unavailable");

        var deletion = provider.RemoveAsync("{entry}");
        Assert.False(deletion.IsCompleted);
        pending.SetException(failure);

        var observed = await Assert.ThrowsAsync<RedisConnectionException>(
            () => deletion.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Same(failure, observed);
    }

    [Fact]
    public async Task AddRedisCache_Routes_Outlet_Operations_To_Redis_Until_A_Later_Selection() {
        var database = new Mock<IDatabase>(MockBehavior.Loose);
        var multiplexer = new Mock<IConnectionMultiplexer>(MockBehavior.Strict);
        multiplexer.Setup(connection => connection.GetDatabase(-1, null)).Returns(database.Object);

        var services = new ServiceCollection();
        services.AddSingleton(multiplexer.Object);
        services.AddRedisCache();

        using (var provider = services.BuildServiceProvider()) {
            await provider.GetRequiredService<ICacheProvider>().SetAsync("{entry}", [1], new());
        }
        database.Verify(
            db => db.ScriptEvaluateAsync(
                It.IsAny<string>(),
                It.Is<RedisKey[]>(keys => keys.Length == 2 && keys[0] == "{entry}"),
                It.IsAny<RedisValue[]>(),
                It.IsAny<CommandFlags>()),
            Times.Once);

        var writes = new List<string>();
        var replacement = new Mock<ICacheProvider>();
        replacement.Setup(cache => cache.SetAsync(
                        It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CacheEntryOptions>(),
                        It.IsAny<CancellationToken>()))
                   .Callback((string key, byte[] _, CacheEntryOptions _, CancellationToken _) => writes.Add(key))
                   .Returns(Task.CompletedTask);
        services.AddCacheProvider(_ => replacement.Object);

        using (var provider = services.BuildServiceProvider()) {
            await provider.GetRequiredService<ICacheProvider>().SetAsync("{other}", [2], new());
        }
        Assert.Equal("{other}", Assert.Single(writes));
        database.Verify(
            db => db.ScriptEvaluateAsync(
                It.IsAny<string>(),
                It.Is<RedisKey[]>(keys => keys[0] == "{other}"),
                It.IsAny<RedisValue[]>(),
                It.IsAny<CommandFlags>()),
            Times.Never);
    }

    private static RedisCacheProvider CreateProvider(Mock<IDatabase> database) {
        var multiplexer = new Mock<IConnectionMultiplexer>(MockBehavior.Strict);
        multiplexer.Setup(connection => connection.GetDatabase(-1, null)).Returns(database.Object);
        return new(multiplexer.Object);
    }
}
