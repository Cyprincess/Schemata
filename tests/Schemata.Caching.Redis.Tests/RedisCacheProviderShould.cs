using System;
using System.Collections.Generic;
using System.Linq;
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
        var stored = new HashSet<RedisKey> {
            RedisConstants.DataPrefix + "{entry}",
            RedisConstants.MetaPrefix + "{entry}",
            "other"
        };
        var database = new Mock<IDatabase>(MockBehavior.Strict);
        database.Setup(db => db.KeyDeleteAsync(It.IsAny<RedisKey[]>(), CommandFlags.None))
                .ReturnsAsync((RedisKey[] deletedKeys, CommandFlags _) => {
                    var count = 0L;
                    foreach (var key in deletedKeys) {
                        if (stored.Remove(key)) {
                            count++;
                        }
                    }

                    return count;
                });
        var provider = CreateProvider(database);

        var deletion = clearCollection
            ? provider.CollectionClearAsync("{entry}")
            : provider.RemoveAsync("{entry}");
        await deletion.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.DoesNotContain((RedisKey)(RedisConstants.DataPrefix + "{entry}"), stored);
        Assert.DoesNotContain((RedisKey)(RedisConstants.MetaPrefix + "{entry}"), stored);
        Assert.Equal((RedisKey)"other", Assert.Single(stored));
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
            () => deletion.WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.Same(failure, observed);
    }

    [Fact]
    public async Task Remove_Logical_Key_Leaves_Entry_Named_After_Another_Entry_Metadata_Untouched() {
        var (provider, _) = CreateStoreProvider(new FakeClock());
        var value    = new byte[] { 0x01 };
        var suffixed = new byte[] { 0x02 };

        await provider.SetAsync("foo", value, Sliding(10));
        await provider.SetAsync("foo:__meta__", suffixed, Sliding(10));

        await provider.RemoveAsync("foo");

        Assert.Null(await provider.GetAsync("foo"));
        Assert.Equal(suffixed, await provider.GetAsync("foo:__meta__"));
    }

    [Fact]
    public async Task Get_Refresh_Extends_Only_The_Addressed_Logical_Key_Pair() {
        var clock      = new FakeClock();
        var (provider, _) = CreateStoreProvider(clock);
        var value      = new byte[] { 0x01 };
        var suffixed   = new byte[] { 0x02 };

        await provider.SetAsync("foo", value, Sliding(10));
        await provider.SetAsync("foo:__meta__", suffixed, Sliding(10));

        clock.UtcNowValue = clock.UtcNowValue.AddSeconds(6);
        Assert.Equal(value, await provider.GetAsync("foo"));

        clock.UtcNowValue = clock.UtcNowValue.AddSeconds(6);
        Assert.Equal(value, await provider.GetAsync("foo"));
        Assert.Null(await provider.GetAsync("foo:__meta__"));
    }

    [Fact]
    public async Task Refresh_Extends_Sliding_Only_Up_To_The_Absolute_Deadline() {
        var clock = new FakeClock();
        var (provider, _) = CreateStoreProvider(clock);
        await provider.SetAsync("k", new byte[] { 0x01 }, new() {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(3),
            SlidingExpiration = TimeSpan.FromMinutes(1),
        });

        clock.UtcNowValue = clock.UtcNowValue.AddSeconds(50);
        await provider.RefreshAsync("k");

        clock.UtcNowValue = clock.UtcNowValue.AddSeconds(50);
        Assert.Equal(new byte[] { 0x01 }, await provider.GetAsync("k"));

        clock.UtcNowValue = clock.UtcNowValue.AddMinutes(3);
        await provider.RefreshAsync("k");
        Assert.Null(await provider.GetAsync("k"));
    }

    [Fact]
    public async Task Refresh_Extends_A_Pure_Sliding_Entry() {
        var clock = new FakeClock();
        var (provider, _) = CreateStoreProvider(clock);
        await provider.SetAsync("k", new byte[] { 0x01 }, Sliding(10));

        clock.UtcNowValue = clock.UtcNowValue.AddSeconds(6);
        await provider.RefreshAsync("k");

        clock.UtcNowValue = clock.UtcNowValue.AddSeconds(6);
        Assert.Equal(new byte[] { 0x01 }, await provider.GetAsync("k"));

        clock.UtcNowValue = clock.UtcNowValue.AddSeconds(11);
        Assert.Null(await provider.GetAsync("k"));
    }

    [Fact]
    public async Task Refresh_Extends_Only_The_Addressed_Logical_Key_Pair() {
        var clock = new FakeClock();
        var (provider, _) = CreateStoreProvider(clock);
        var first  = new byte[] { 0x01 };
        var second = new byte[] { 0x02 };

        await provider.SetAsync("foo", first, Sliding(10));
        await provider.SetAsync("foo:__meta__", second, Sliding(10));

        clock.UtcNowValue = clock.UtcNowValue.AddSeconds(6);
        await provider.RefreshAsync("foo");

        clock.UtcNowValue = clock.UtcNowValue.AddSeconds(6);
        Assert.Equal(first, await provider.GetAsync("foo"));
        Assert.Null(await provider.GetAsync("foo:__meta__"));
    }

    [Fact]
    public async Task Refresh_Snapshot_Metadata_Race_Loses_To_A_Concurrent_Rewrite() {
        var clock = new FakeClock();
        FakeRedisStore? store = null;
        var (provider, created) = CreateStoreProvider(
            clock,
            onLiveMetaRead: key => {
                var logical = ((string)key!)[RedisConstants.MetaPrefix.Length..];
                store!.OverwritePair(
                    RedisConstants.DataPrefix + logical,
                    key,
                    new byte[] { 0x01 },
                    new byte[] { 0xFF },
                    2000);
            });
        store = created;

        await provider.SetAsync("k", new byte[] { 0x01 }, Sliding(10));

        clock.UtcNowValue = clock.UtcNowValue.AddSeconds(6);
        await provider.RefreshAsync("k");

        clock.UtcNowValue = clock.UtcNowValue.AddSeconds(3);
        Assert.Null(await provider.GetAsync("k"));
    }

    [Fact]
    public async Task Refresh_After_Expiry_Does_Not_Resurrect_The_Entry() {
        var clock = new FakeClock();
        var (provider, _) = CreateStoreProvider(clock);
        await provider.SetAsync("k", new byte[] { 0x01 }, Sliding(10));

        clock.UtcNowValue = clock.UtcNowValue.AddSeconds(11);
        await provider.RefreshAsync("k");

        Assert.Null(await provider.GetAsync("k"));
    }

    [Fact]
    public async Task Refresh_After_Removed_Entry_Does_Not_Resurrect_It() {
        var clock = new FakeClock();
        var (provider, _) = CreateStoreProvider(clock);
        await provider.SetAsync("k", new byte[] { 0x01 }, Sliding(10));

        await provider.RemoveAsync("k");
        await provider.RefreshAsync("k");

        Assert.Null(await provider.GetAsync("k"));
    }

    [Fact]
    public async Task TryReplace_CompareAndSet_Wins_And_Loses_Within_The_Addressed_Logical_Key() {
        var (provider, _) = CreateStoreProvider(new FakeClock());
        var first  = new byte[] { 0x01 };
        var second = new byte[] { 0x02 };
        var third  = new byte[] { 0x03 };
        var stale  = new byte[] { 0x04 };
        var options = new CacheEntryOptions();

        await provider.SetAsync("foo", first, options);
        await provider.SetAsync("foo:__meta__", second, options);

        Assert.True(await provider.TryReplaceAsync("foo", first, third, options));
        Assert.Equal(third, await provider.GetAsync("foo"));
        Assert.Equal(second, await provider.GetAsync("foo:__meta__"));

        Assert.False(await provider.TryReplaceAsync("foo", first, stale, options));
        Assert.Equal(third, await provider.GetAsync("foo"));
    }

    [Fact]
    public async Task TryRemove_CompareAndDelete_Deletes_Only_The_Addressed_Logical_Key_Pair() {
        var (provider, _) = CreateStoreProvider(new FakeClock());
        var first  = new byte[] { 0x01 };
        var second = new byte[] { 0x02 };
        var options = new CacheEntryOptions();

        await provider.SetAsync("foo", first, options);
        await provider.SetAsync("foo:__meta__", second, options);

        Assert.False(await provider.TryRemoveAsync("foo", second));
        Assert.Equal(first, await provider.GetAsync("foo"));
        Assert.Equal(second, await provider.GetAsync("foo:__meta__"));

        Assert.True(await provider.TryRemoveAsync("foo", first));
        Assert.Null(await provider.GetAsync("foo"));
        Assert.Equal(second, await provider.GetAsync("foo:__meta__"));
    }

    [Fact]
    public async Task TryAdd_Absence_Check_Ignores_Another_Entry_Metadata_Key() {
        var (provider, _) = CreateStoreProvider(new FakeClock());
        var first  = new byte[] { 0x01 };
        var second = new byte[] { 0x02 };
        var third  = new byte[] { 0x03 };
        var options = new CacheEntryOptions();

        await provider.SetAsync("foo", first, options);

        Assert.True(await provider.TryAddAsync("foo:__meta__", second, options));
        Assert.False(await provider.TryAddAsync("foo:__meta__", third, options));
        Assert.Equal(second, await provider.GetAsync("foo:__meta__"));
        Assert.Equal(first, await provider.GetAsync("foo"));
    }

    [Fact]
    public async Task Collection_Operations_Stay_Disjoint_From_Entry_Named_After_Another_Entry_Metadata() {
        var (provider, _) = CreateStoreProvider(new FakeClock(), singleValueScriptRemovesMembers: true);
        var suffixed = new byte[] { 0x02 };

        await provider.CollectionAddAsync("foo", "m1", Sliding(10));
        await provider.SetAsync("foo:__meta__", suffixed, Sliding(10));

        Assert.Equal(new[] { "m1" }, await provider.CollectionMembersAsync("foo"));
        Assert.Equal(suffixed, await provider.GetAsync("foo:__meta__"));

        await provider.CollectionRemoveAsync("foo", new[] { "m1" });

        Assert.Null(await provider.CollectionMembersAsync("foo"));
        Assert.Equal(suffixed, await provider.GetAsync("foo:__meta__"));

        await provider.CollectionAddAsync("foo:__meta__:set", "m2", Sliding(10));

        Assert.Null(await provider.CollectionMembersAsync("foo"));
        Assert.Equal(new[] { "m2" }, await provider.CollectionMembersAsync("foo:__meta__:set"));
        Assert.Equal(suffixed, await provider.GetAsync("foo:__meta__"));
    }

    [Fact]
    public async Task CollectionRemove_Bulk_Members_Removes_Every_Member_And_Clears_Metadata_When_Empty() {
        var (provider, _) = CreateStoreProvider(new FakeClock());
        const int count = 600;
        for (var i = 0; i < count; i++) {
            await provider.CollectionAddAsync("set", "member-" + i, Sliding(10));
        }

        var members = Enumerable.Range(0, count).Select(i => "member-" + i).ToArray();
        await provider.CollectionRemoveAsync("set", members);

        Assert.Null(await provider.CollectionMembersAsync("set"));
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
                It.Is<RedisKey[]>(keys => keys.Length == 2 && keys[0] == RedisConstants.DataPrefix + "{entry}"),
                It.IsAny<RedisValue[]>(),
                It.IsAny<CommandFlags>()),
            Times.Once);

        var writes = new List<string>();
        var replacement = new Mock<ICacheBackend>();
        replacement.Setup(cache => cache.SetAsync(
                        It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CacheEntryOptions>(),
                        It.IsAny<CancellationToken>()))
                   .Callback((string key, byte[] _, CacheEntryOptions _, CancellationToken _) => writes.Add(key))
                   .Returns(Task.CompletedTask);
        services.AddCacheBackend(_ => replacement.Object);

        using (var provider = services.BuildServiceProvider()) {
            await provider.GetRequiredService<ICacheProvider>().SetAsync("{other}", [2], new());
        }
        Assert.Equal("{other}", Assert.Single(writes));
        database.Verify(
            db => db.ScriptEvaluateAsync(
                It.IsAny<string>(),
                It.Is<RedisKey[]>(keys => keys[0] == RedisConstants.DataPrefix + "{other}"),
                It.IsAny<RedisValue[]>(),
                It.IsAny<CommandFlags>()),
            Times.Never);
    }

    [Fact]
    public void AddRedisCache_Exposes_No_Synchronous_Capability() {
        var multiplexer = new Mock<IConnectionMultiplexer>(MockBehavior.Strict);
        var services = new ServiceCollection();
        services.AddSingleton(multiplexer.Object);
        services.AddRedisCache();
        using var provider = services.BuildServiceProvider();

        Assert.Null(provider.GetService<ISynchronousCacheBackend>());
    }

    private static RedisCacheBackend CreateProvider(Mock<IDatabase> database) {
        var multiplexer = new Mock<IConnectionMultiplexer>(MockBehavior.Strict);
        multiplexer.Setup(connection => connection.GetDatabase(-1, null)).Returns(database.Object);
        return new(multiplexer.Object);
    }

    private static (RedisCacheBackend Provider, FakeRedisStore Store) CreateStoreProvider(
        FakeClock         clock,
        bool              singleValueScriptRemovesMembers = false,
        Action<RedisKey>? onLiveMetaRead                  = null
    ) {
        var store = new FakeRedisStore(clock, singleValueScriptRemovesMembers);
        var database = new Mock<IDatabase>(MockBehavior.Strict);
        database.Setup(db => db.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync((RedisKey key, CommandFlags _) => {
                    var value = store.GetString(key);
                    if (onLiveMetaRead is not null
                     && !value.IsNull
                     && ((string)key!).StartsWith(RedisConstants.MetaPrefix, StringComparison.Ordinal)) {
                        onLiveMetaRead(key);
                    }

                    return value;
                });
        database.Setup(db => db.ScriptEvaluateAsync(
                       It.IsAny<string>(),
                       It.IsAny<RedisKey[]>(),
                       It.IsAny<RedisValue[]>(),
                       It.IsAny<CommandFlags>()))
                .ReturnsAsync(
                    (string _, RedisKey[] keys, RedisValue[] values, CommandFlags _) => RedisResult.Create(store.Evaluate(keys, values)));
        database.Setup(db => db.KeyDeleteAsync(It.IsAny<RedisKey[]>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync((RedisKey[] keys, CommandFlags _) => store.Delete(keys));
        database.Setup(db => db.SetMembersAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync((RedisKey key, CommandFlags _) => store.Members(key));
        var multiplexer = new Mock<IConnectionMultiplexer>(MockBehavior.Strict);
        multiplexer.Setup(connection => connection.GetDatabase(-1, null)).Returns(database.Object);
        return (new RedisCacheBackend(multiplexer.Object, clock), store);
    }

    private static CacheEntryOptions Sliding(int seconds) {
        return new() { SlidingExpiration = TimeSpan.FromSeconds(seconds) };
    }

    private sealed class FakeClock : TimeProvider
    {
        public DateTimeOffset UtcNowValue { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() { return UtcNowValue; }
    }

    /// <summary>
    ///     In-memory Redis backend state. Script argument shapes are interpreted from the provider's
    ///     command contract, never from key values, so every effect lands wherever the provider's
    ///     framing sends it and tests observe outcomes through subsequent reads.
    /// </summary>
    private sealed class FakeRedisStore(FakeClock clock, bool singleValueScriptRemovesMembers)
    {
        private readonly Dictionary<RedisKey, RedisValue>      _strings  = new();
        private readonly Dictionary<RedisKey, List<string>>    _sets     = new();
        private readonly Dictionary<RedisKey, DateTimeOffset?> _expiries = new();

        private bool IsExpired(RedisKey key) {
            return _expiries.TryGetValue(key, out var expiry)
                && expiry.HasValue
                && expiry.Value <= clock.GetUtcNow();
        }

        private void Purge(RedisKey key) {
            _strings.Remove(key);
            _sets.Remove(key);
            _expiries.Remove(key);
        }

        public bool Exists(RedisKey key) {
            if (IsExpired(key)) {
                Purge(key);
            }

            return _strings.ContainsKey(key) || _sets.ContainsKey(key);
        }

        public RedisValue GetString(RedisKey key) {
            if (IsExpired(key)) {
                Purge(key);
            }

            return _strings.TryGetValue(key, out var value) ? value : RedisValue.Null;
        }

        private void SetString(RedisKey key, RedisValue value, long ttlMilliseconds) {
            _sets.Remove(key);
            _strings[key] = value;
            _expiries[key] = ttlMilliseconds >= 0 ? clock.GetUtcNow().AddMilliseconds(ttlMilliseconds) : null;
        }

        private void SAdd(RedisKey key, string member, long ttlMilliseconds) {
            if (!_sets.TryGetValue(key, out var members)) {
                _strings.Remove(key);
                members = [];
                _sets[key] = members;
            }

            members.Add(member);
            _expiries[key] = ttlMilliseconds >= 0 ? clock.GetUtcNow().AddMilliseconds(ttlMilliseconds) : null;
        }

        private void SRem(RedisKey key, string member) {
            if (_sets.TryGetValue(key, out var members) && members.Remove(member) && members.Count == 0) {
                Purge(key);
            }
        }

        private void PExpire(RedisKey key, long ttlMilliseconds) {
            if (!Exists(key) || ttlMilliseconds < 0) {
                return;
            }

            _expiries[key] = clock.GetUtcNow().AddMilliseconds(ttlMilliseconds);
        }

        public void OverwritePair(RedisKey data, RedisKey meta, RedisValue value, RedisValue metadata, long ttlMilliseconds) {
            SetString(data, value, ttlMilliseconds);
            SetString(meta, metadata, ttlMilliseconds);
        }

        public RedisValue[] Members(RedisKey key) {
            if (IsExpired(key)) {
                Purge(key);
            }

            return _sets.TryGetValue(key, out var members)
                ? [.. members.Select(m => (RedisValue)m)]
                : [];
        }

        public long Delete(RedisKey[] keys) {
            var count = 0L;
            foreach (var key in keys) {
                if (Exists(key)) {
                    Purge(key);
                    count++;
                }
            }

            return count;
        }

        public long Evaluate(RedisKey[] keys, RedisValue[] values) {
            switch (values.Length) {
                // WriteScript: ["SET"|"SADD", payload, TTL, metadata] over [data, meta].
                case 4 when values[0] == "SET" || values[0] == "SADD":
                    var writeTtl = (long)values[2];
                    if (values[0] == "SET") {
                        SetString(keys[0], values[1], writeTtl);
                    } else {
                        SAdd(keys[0], (string)values[1]!, writeTtl);
                    }

                    SetString(keys[1], values[3], writeTtl);
                    return 1;

                // ReplaceScript: [expected, replacement, TTL, metadata] over [data, meta].
                case 4:
                    if (!SameValue(GetString(keys[0]), values[0])) {
                        return 0;
                    }

                    var replaceTtl = (long)values[2];
                    SetString(keys[0], values[1], replaceTtl);
                    SetString(keys[1], values[3], replaceTtl);
                    return 1;

                // AddScript: [payload, TTL, metadata] over [data, meta].
                case 3:
                    if (Exists(keys[0])) {
                        return 0;
                    }

                    var addTtl = (long)values[1];
                    SetString(keys[0], values[0], addTtl);
                    SetString(keys[1], values[2], addTtl);
                    return 1;

                // RefreshScript: [metadata snapshot, TTL] over [data, meta].
                case 2:
                    if (Exists(keys[0]) && SameValue(GetString(keys[1]), values[0])) {
                        PExpire(keys[0], (long)values[1]);
                        PExpire(keys[1], (long)values[1]);
                    }

                    return 1;

                case 1 when singleValueScriptRemovesMembers:
                    // RemoveMembersScript: one member over [data, meta].
                    SRem(keys[0], (string)values[0]!);
                    if (!Exists(keys[0])) {
                        Purge(keys[1]);
                    }

                    return 1;

                // RemoveScript: [expected] over [data, meta].
                case 1:
                    if (!SameValue(GetString(keys[0]), values[0])) {
                        return 0;
                    }

                    Purge(keys[0]);
                    Purge(keys[1]);
                    return 1;

                // RemoveMembersScript: member batch over [data, meta].
                default:
                    if (values.Length <= 1) {
                        throw new InvalidOperationException("Unexpected script argument shape.");
                    }

                    foreach (var value in values) {
                        SRem(keys[0], (string)value!);
                    }

                    if (!Exists(keys[0])) {
                        Purge(keys[1]);
                    }

                    return 1;
            }
        }

        private static bool SameValue(RedisValue left, RedisValue right) {
            if (left.IsNull || right.IsNull) {
                return left.IsNull && right.IsNull;
            }

            return ((byte[])left!).AsSpan().SequenceEqual((byte[])right!);
        }
    }
}
