using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Moq;
using Schemata.Caching.Skeleton;
using Xunit;

namespace Schemata.Caching.Distributed.Tests;

public class DistributedCacheProviderShould
{
    [Fact]
    public async Task TryAddAsync_AbsentKey_InsertsAndReturnsTrue() {
        var provider = CreateProvider();

        var added = await provider.TryAddAsync("k", Bytes("v"), NewOptions());

        Assert.True(added);
        Assert.Equal(Bytes("v"), await provider.GetAsync("k"));
    }

    [Fact]
    public async Task TryAddAsync_PresentKey_ReturnsFalseAndKeepsExisting() {
        var provider = CreateProvider();
        await provider.SetAsync("k", Bytes("first"), NewOptions());

        var added = await provider.TryAddAsync("k", Bytes("second"), NewOptions());

        Assert.False(added);
        Assert.Equal(Bytes("first"), await provider.GetAsync("k"));
    }

    [Fact]
    public async Task TryReplaceAsync_CurrentEqualsExpected_ReplacesAndReturnsTrue() {
        var provider = CreateProvider();
        await provider.SetAsync("k", Bytes("a"), NewOptions());

        var swapped = await provider.TryReplaceAsync("k", Bytes("a"), Bytes("b"), NewOptions());

        Assert.True(swapped);
        Assert.Equal(Bytes("b"), await provider.GetAsync("k"));
    }

    [Fact]
    public async Task TryReplaceAsync_CurrentDiffersFromExpected_ReturnsFalseAndKeepsCurrent() {
        var provider = CreateProvider();
        await provider.SetAsync("k", Bytes("a"), NewOptions());

        var swapped = await provider.TryReplaceAsync("k", Bytes("x"), Bytes("b"), NewOptions());

        Assert.False(swapped);
        Assert.Equal(Bytes("a"), await provider.GetAsync("k"));
    }

    [Fact]
    public async Task TryReplaceAsync_AbsentKey_ReturnsFalse() {
        var provider = CreateProvider();

        var swapped = await provider.TryReplaceAsync("k", Bytes("a"), Bytes("b"), NewOptions());

        Assert.False(swapped);
        Assert.Null(await provider.GetAsync("k"));
    }

    [Fact]
    public async Task TryRemoveAsync_CurrentEqualsExpected_RemovesAndReturnsTrue() {
        var provider = CreateProvider();
        await provider.SetAsync("k", Bytes("a"), NewOptions());

        var removed = await provider.TryRemoveAsync("k", Bytes("a"));

        Assert.True(removed);
        Assert.Null(await provider.GetAsync("k"));
    }

    [Fact]
    public async Task TryRemoveAsync_CurrentDiffersFromExpected_ReturnsFalseAndKeepsEntry() {
        var provider = CreateProvider();
        await provider.SetAsync("k", Bytes("a"), NewOptions());

        var removed = await provider.TryRemoveAsync("k", Bytes("x"));

        Assert.False(removed);
        Assert.Equal(Bytes("a"), await provider.GetAsync("k"));
    }

    [Fact]
    public async Task TryRemoveAsync_AbsentKey_ReturnsFalse() {
        var provider = CreateProvider();

        var removed = await provider.TryRemoveAsync("k", Bytes("a"));

        Assert.False(removed);
    }

    [Fact]
    public async Task TryAddAsync_ParallelSameKey_ExactlyOneSucceeds() {
        var provider = CreateProvider();
        const int count = 64;

        var tasks = Enumerable.Range(0, count)
                              .Select(i => Task.Run(() => provider.TryAddAsync("k", Bytes("v" + i), NewOptions())))
                              .ToArray();

        var results = await Task.WhenAll(tasks);

        Assert.Equal(1, results.Count(r => r));
    }

    [Fact]
    public async Task MixedParallelOperations_CompleteWithoutDeadlock() {
        var provider = CreateProvider();
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

    private static DistributedCacheProvider CreateProvider() {
        var values = new ConcurrentDictionary<string, byte[]>();
        var cache  = new Mock<IDistributedCache>();
        cache.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((string key, CancellationToken cancellationToken) => values.TryGetValue(key, out var value) ? value : null);
        cache.Setup(c => c.SetAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()))
             .Returns((string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken cancellationToken) => {
                  values[key] = value;
                  return Task.CompletedTask;
              });
        cache.Setup(c => c.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .Returns((string key, CancellationToken cancellationToken) => {
                  values.TryRemove(key, out _);
                  return Task.CompletedTask;
              });
        return new(cache.Object);
    }

    private static CacheEntryOptions NewOptions() {
        return new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) };
    }

    private static byte[] Bytes(string value) { return Encoding.UTF8.GetBytes(value); }
}
