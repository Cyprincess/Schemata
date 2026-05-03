using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Schemata.Abstractions;
using Schemata.Entity.Cache.Tests.Fixtures;
using Xunit;

namespace Schemata.Entity.Cache.Tests;

public class ReverseIndexShould
{
    [Fact]
    public void BuildKey_FollowsFrameworkGuidEntityTypePkLayout() {
        var entity   = new Student { Id = 42 };
        var expected = $"{SchemataConstants.Schemata}\u001e{typeof(Student).FullName}\u001e42";

        var key = ReverseIndex.BuildKey(typeof(Student), entity);

        Assert.Equal(expected, key);
    }

    [Fact]
    public void BuildKey_WhenEntityHasNoKeyProperty_ReturnsNull() {
        var key = ReverseIndex.BuildKey(typeof(Keyless), new Keyless());

        Assert.Null(key);
    }

    [Fact]
    public async Task ReadSetAsync_WhenIndexMissing_ReturnsEmpty() {
        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

        var set = await ReverseIndex.ReadSetAsync(cache, "nonexistent", CancellationToken.None);

        Assert.Empty(set);
    }

    [Fact]
    public async Task ReadSetAsync_RoundTripsWrittenEntries() {
        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

        await ReverseIndex.WriteSetAsync(cache, "index", ["a", "b", "c"], TimeSpan.FromMinutes(1),
                                         CancellationToken.None);

        var set = await ReverseIndex.ReadSetAsync(cache, "index", CancellationToken.None);

        Assert.Contains("a", set);
        Assert.Contains("b", set);
        Assert.Contains("c", set);
    }

    #region Nested type: Keyless

    private sealed class Keyless
    {
        public string? Whatever { get; set; }
    }

    #endregion
}
