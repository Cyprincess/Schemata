using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Schemata.Caching.Memory;
using Schemata.Caching.Skeleton;
using Xunit;

namespace Schemata.Caching.Memory.Integration.Tests;

[Trait("Layer", "Integration")]
public class MemoryCacheProviderShould
{
    [Fact]
    public async Task TryAddAsync_AbsentKey_InsertsAndReturnsTrue() {
        using var provider = CreateProvider();

        var added = await provider.TryAddAsync("k", Bytes("v"), NewOptions());

        Assert.True(added);
        Assert.Equal(Bytes("v"), await provider.GetAsync("k"));
    }

    [Fact]
    public async Task TryAddAsync_PresentKey_ReturnsFalseAndKeepsExisting() {
        using var provider = CreateProvider();
        await provider.SetAsync("k", Bytes("first"), NewOptions());

        var added = await provider.TryAddAsync("k", Bytes("second"), NewOptions());

        Assert.False(added);
        Assert.Equal(Bytes("first"), await provider.GetAsync("k"));
    }

    [Fact]
    public async Task TryReplaceAsync_CurrentEqualsExpected_ReplacesAndReturnsTrue() {
        using var provider = CreateProvider();
        await provider.SetAsync("k", Bytes("a"), NewOptions());

        var swapped = await provider.TryReplaceAsync("k", Bytes("a"), Bytes("b"), NewOptions());

        Assert.True(swapped);
        Assert.Equal(Bytes("b"), await provider.GetAsync("k"));
    }

    [Fact]
    public async Task TryReplaceAsync_CurrentDiffersFromExpected_ReturnsFalseAndKeepsCurrent() {
        using var provider = CreateProvider();
        await provider.SetAsync("k", Bytes("a"), NewOptions());

        var swapped = await provider.TryReplaceAsync("k", Bytes("x"), Bytes("b"), NewOptions());

        Assert.False(swapped);
        Assert.Equal(Bytes("a"), await provider.GetAsync("k"));
    }

    [Fact]
    public async Task TryReplaceAsync_AbsentKey_ReturnsFalse() {
        using var provider = CreateProvider();

        var swapped = await provider.TryReplaceAsync("k", Bytes("a"), Bytes("b"), NewOptions());

        Assert.False(swapped);
        Assert.Null(await provider.GetAsync("k"));
    }

    [Fact]
    public async Task TryRemoveAsync_CurrentEqualsExpected_RemovesAndReturnsTrue() {
        using var provider = CreateProvider();
        await provider.SetAsync("k", Bytes("a"), NewOptions());

        var removed = await provider.TryRemoveAsync("k", Bytes("a"));

        Assert.True(removed);
        Assert.Null(await provider.GetAsync("k"));
    }

    [Fact]
    public async Task TryRemoveAsync_CurrentDiffersFromExpected_ReturnsFalseAndKeepsEntry() {
        using var provider = CreateProvider();
        await provider.SetAsync("k", Bytes("a"), NewOptions());

        var removed = await provider.TryRemoveAsync("k", Bytes("x"));

        Assert.False(removed);
        Assert.Equal(Bytes("a"), await provider.GetAsync("k"));
    }

    [Fact]
    public async Task TryRemoveAsync_AbsentKey_ReturnsFalse() {
        using var provider = CreateProvider();

        var removed = await provider.TryRemoveAsync("k", Bytes("a"));

        Assert.False(removed);
    }

    [Fact]
    public async Task TryAddAsync_ParallelSameKey_ExactlyOneSucceeds() {
        using var provider = CreateProvider();
        const int count = 64;

        var tasks = Enumerable.Range(0, count)
                              .Select(i => Task.Run(() => provider.TryAddAsync("k", Bytes("v" + i), NewOptions())))
                              .ToArray();

        var results = await Task.WhenAll(tasks);

        Assert.Equal(1, results.Count(r => r));
    }

    [Fact]
    public async Task MixedParallelOperations_CompleteWithoutDeadlock() {
        using var provider = CreateProvider();
        const int count = 128;

        // Disjoint keys per task make every compare-and-swap succeed, so a false
        // return or a surviving entry is a defect rather than a scheduling artifact.
        var outcomes = await Task.WhenAll(Enumerable.Range(0, count).Select(i => Task.Run(async () => {
            var key = "key-" + i;

            var added    = await provider.TryAddAsync(key, Bytes("v" + i), NewOptions());
            var replaced = await provider.TryReplaceAsync(key, Bytes("v" + i), Bytes("r"), NewOptions());
            var observed = await provider.GetAsync(key);
            var removed  = await provider.TryRemoveAsync(key, Bytes("r"));

            return (Added: added, Replaced: replaced, Observed: observed, Removed: removed);
        }))).WaitAsync(TimeSpan.FromSeconds(30));

        Assert.All(outcomes, outcome => {
            Assert.True(outcome.Added);
            Assert.True(outcome.Replaced);
            Assert.True(outcome.Removed);
            Assert.Equal(Bytes("r"), outcome.Observed);
        });

        var survivors = await Task.WhenAll(Enumerable.Range(0, count)
                                                  .Select(i => provider.GetAsync("key-" + i)));
        Assert.All(survivors, survivor => Assert.Null(survivor));
    }

    [Fact]
    public async Task Concurrent_Remove_And_Refresh_Leave_The_Entry_Removed_Or_Original() {
        using var provider = CreateProvider();
        await provider.SetAsync("k", Bytes("value"), new() { SlidingExpiration = TimeSpan.FromSeconds(60) });
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        const int count = 32;

        var refreshes = Enumerable.Range(0, count).Select(_ => Task.Run(async () => {
            await start.Task;
            await provider.RefreshAsync("k");
        })).ToArray();
        var removes = Enumerable.Range(0, count).Select(_ => Task.Run(async () => {
            await start.Task;
            await provider.RemoveAsync("k");
        })).ToArray();

        start.SetResult();
        await Task.WhenAll(refreshes.Concat(removes)).WaitAsync(TimeSpan.FromSeconds(30));

        var survivor = await provider.GetAsync("k");
        Assert.True(survivor is null || survivor.AsSpan().SequenceEqual(Bytes("value")));
    }

    [Fact]
    public async Task Sliding_Refresh_Stops_At_Absolute_Deadline() {
        var time = new FakeTimeProvider();
        using var provider = new MemoryCacheBackend(time);
        await provider.SetAsync("k", Bytes("value"), new() {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(10),
            SlidingExpiration = TimeSpan.FromSeconds(4),
        });
        for (var i = 0; i < 3; i++) {
            time.Advance(TimeSpan.FromSeconds(3));
            Assert.Equal(Bytes("value"), await provider.GetAsync("k"));
        }
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Null(await provider.GetAsync("k"));
        Assert.True(await provider.TryAddAsync("k", Bytes("next"), NewOptions()));
        Assert.False(await provider.TryRemoveAsync("k", Bytes("value")));
        Assert.Equal(Bytes("next"), await provider.GetAsync("k"));
    }

    [Fact]
    public async Task Refresh_Extends_Sliding_Window_Only_Up_To_The_Absolute_Deadline() {
        var time = new FakeTimeProvider();
        using var provider = new MemoryCacheBackend(time);
        await provider.SetAsync("k", Bytes("value"), new() {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(3),
            SlidingExpiration = TimeSpan.FromMinutes(1),
        });

        time.Advance(TimeSpan.FromSeconds(50));
        await provider.RefreshAsync("k");

        time.Advance(TimeSpan.FromSeconds(50));
        Assert.Equal(Bytes("value"), await provider.GetAsync("k"));

        time.Advance(TimeSpan.FromMinutes(3));
        await provider.RefreshAsync("k");
        Assert.Null(await provider.GetAsync("k"));
    }

    [Fact]
    public async Task Refresh_Keeps_A_Pure_Sliding_Entry_Alive_Within_Its_Window() {
        var time = new FakeTimeProvider();
        using var provider = new MemoryCacheBackend(time);
        await provider.SetAsync("k", Bytes("value"), new() { SlidingExpiration = TimeSpan.FromSeconds(4) });

        for (var i = 0; i < 4; i++) {
            time.Advance(TimeSpan.FromSeconds(3));
            await provider.RefreshAsync("k");
        }

        Assert.Equal(Bytes("value"), await provider.GetAsync("k"));
    }

    [Fact]
    public async Task Refresh_Does_Not_Resurrect_An_Entry_Past_Its_Sliding_Window() {
        var time = new FakeTimeProvider();
        using var provider = new MemoryCacheBackend(time);
        await provider.SetAsync("k", Bytes("value"), new() { SlidingExpiration = TimeSpan.FromSeconds(4) });

        time.Advance(TimeSpan.FromSeconds(5));
        await provider.RefreshAsync("k");

        Assert.Null(await provider.GetAsync("k"));
    }

    [Fact]
    public async Task Caller_Mutation_Cannot_Change_Conditional_Write_Identity() {
        using var provider = CreateProvider();
        var input = Bytes("a");
        await provider.SetAsync("k", input, NewOptions());
        input[0] = (byte)'b';
        var output = await provider.GetAsync("k");
        Assert.Equal(Bytes("a"), output);
        output![0] = (byte)'c';
        Assert.True(await provider.TryReplaceAsync("k", Bytes("a"), Bytes("d"), NewOptions()));
        Assert.Equal(Bytes("d"), await provider.GetAsync("k"));
    }

    [Fact]
    public async Task Removing_Old_Members_Preserves_New_Member_And_Expiry() {
        var time = new FakeTimeProvider();
        using var provider = new MemoryCacheBackend(time);
        await provider.CollectionAddAsync("set", "old", NewOptions());
        await provider.CollectionAddAsync("set", "new", new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(2) });
        await provider.CollectionRemoveAsync("set", new[] { "old", "absent" });
        Assert.Equal("new", Assert.Single((await provider.CollectionMembersAsync("set"))!));
        time.Advance(TimeSpan.FromSeconds(2));
        Assert.Null(await provider.CollectionMembersAsync("set"));
    }

    [Fact]
    public async Task Concurrent_Collection_Mutations_Through_Both_Remove_Overloads_End_Empty() {
        using var provider = CreateProvider();
        const int count = 32;
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var workers = Enumerable.Range(0, count).Select(i => Task.Run(async () => {
            await start.Task;
            await provider.CollectionAddAsync("set", "member-" + i, NewOptions());
            if (i % 2 == 0) {
                await provider.CollectionRemoveAsync("set", new[] { "member-" + i });
            } else {
                await provider.CollectionRemoveAsync("set", "member-" + i);
            }
        })).ToArray();

        start.SetResult();
        await Task.WhenAll(workers).WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Null(await provider.CollectionMembersAsync("set"));
    }

    [Fact]
    public async Task Synchronous_Members_Round_Trip_And_Share_State_With_Asynchronous_Members() {
        using var provider = new MemoryCacheBackend();
        ISynchronousCacheBackend sync = provider;

        sync.Set("k", Bytes("value"), new CacheEntryOptions { SlidingExpiration = TimeSpan.FromSeconds(4) });

        Assert.Equal(Bytes("value"), sync.Get("k"));
        Assert.Equal(Bytes("value"), await provider.GetAsync("k"));

        sync.Refresh("k");
        sync.Remove("k");

        Assert.Null(sync.Get("k"));
        Assert.Null(await provider.GetAsync("k"));
    }

    [Fact]
    public void Synchronous_Refresh_Keeps_A_Sliding_Entry_Alive_Within_Its_Window() {
        var time = new FakeTimeProvider();
        using var provider = new MemoryCacheBackend(time);
        ISynchronousCacheBackend sync = provider;
        sync.Set("k", Bytes("value"), new CacheEntryOptions { SlidingExpiration = TimeSpan.FromSeconds(4) });

        for (var i = 0; i < 4; i++) {
            time.Advance(TimeSpan.FromSeconds(3));
            sync.Refresh("k");
        }

        Assert.Equal(Bytes("value"), sync.Get("k"));

        time.Advance(TimeSpan.FromSeconds(5));
        sync.Refresh("k");
        Assert.Null(sync.Get("k"));
    }

    [Fact]
    public void Host_Registration_Does_Not_Expose_The_Backend_Synchronous_Capability() {
        var services = new ServiceCollection();
        services.AddMemoryCacheProvider();
        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<ICacheProvider>());
        Assert.Null(provider.GetService<ISynchronousCacheBackend>());
    }

    private static MemoryCacheBackend CreateProvider() => new();

    private static CacheEntryOptions NewOptions() {
        return new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) };
    }

    private static byte[] Bytes(string value) { return Encoding.UTF8.GetBytes(value); }
}
