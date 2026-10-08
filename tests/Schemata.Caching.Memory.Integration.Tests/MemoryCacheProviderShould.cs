using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
    public async Task Sliding_Refresh_Stops_At_Absolute_Deadline() {
        var time = new FakeTimeProvider();
        using var provider = new MemoryCacheProvider(time);
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
        using var provider = new MemoryCacheProvider(time);
        await provider.CollectionAddAsync("set", "old", NewOptions());
        await provider.CollectionAddAsync("set", "new", new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(2) });
        await provider.CollectionRemoveAsync("set", new[] { "old", "absent" });
        Assert.Equal("new", Assert.Single((await provider.CollectionMembersAsync("set"))!));
        time.Advance(TimeSpan.FromSeconds(2));
        Assert.Null(await provider.CollectionMembersAsync("set"));
    }

    private static MemoryCacheProvider CreateProvider() => new();

    private static CacheEntryOptions NewOptions() {
        return new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) };
    }

    private static byte[] Bytes(string value) { return Encoding.UTF8.GetBytes(value); }
}
