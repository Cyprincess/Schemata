using System;
using System.Threading.Tasks;
using Schemata.Abstractions.Advisors;
using Schemata.Entity.Cache.Advisors;
using Schemata.Entity.Cache.Tests.Fixtures;
using Xunit;

namespace Schemata.Entity.Cache.Tests.Advisors;

[Trait("Layer", "Unit")]
public class AdviceResultCacheShould
{
    [Fact]
    public async Task Fill_WithoutQuerySnapshot_DoesNotPublishResult() {
        using var cache = new QueryCacheTestContext();
        await cache.Fill(cache.Query<int>(), 7);
        Assert.Equal(AdviseResult.Continue, await cache.Read(cache.Query<int>()));
    }

    [Fact]
    public async Task Fill_WithNullResult_LeavesQueryUncached() {
        using var cache = new QueryCacheTestContext();
        var query = cache.Query<Student>();
        await cache.Read(query);
        await cache.Fill(query, null!);
        Assert.Equal(AdviseResult.Continue, await cache.Read(cache.Query<Student>()));
    }

    [Fact]
    public async Task Fill_UsesConfiguredAbsoluteLifetime_DespiteRepeatedReads() {
        using var cache = new QueryCacheTestContext();
        cache.Options.Ttl = TimeSpan.FromSeconds(7);
        var query = cache.Query<int>();
        await cache.Read(query);
        await cache.Fill(query, 7);

        cache.Elapsed = TimeSpan.FromSeconds(6);
        var hit = cache.Query<int>();
        Assert.Equal(AdviseResult.Handle, await cache.Read(hit));
        Assert.Equal(7, hit.Result);
        cache.Elapsed = TimeSpan.FromSeconds(7);
        Assert.Equal(AdviseResult.Continue, await cache.Read(cache.Query<int>()));
    }

    [Fact]
    public async Task Fill_WhenSuppressed_DoesNotReadOrReplaceCachedResult() {
        using var cache = new QueryCacheTestContext();
        var query = cache.Query<int>();
        await cache.Read(query);
        await cache.Fill(query, 7);

        using (cache.Advice.Use<QueryCacheSuppressed>()) {
            Assert.Equal(AdviseResult.Continue, await cache.Read(cache.Query<int>()));
            await cache.Fill(query, 9);
        }

        var next = cache.Query<int>();
        Assert.Equal(AdviseResult.Handle, await cache.Read(next));
        Assert.Equal(7, next.Result);
    }

    [Fact]
    public async Task Fill_AfterQuerySuppressionEnds_StillRequiresSnapshot() {
        using var cache = new QueryCacheTestContext();
        var query = cache.Query<int>();
        using (cache.Advice.Use<QueryCacheSuppressed>()) {
            await cache.Read(query);
        }
        await cache.Fill(query, 7);
        Assert.Equal(AdviseResult.Continue, await cache.Read(cache.Query<int>()));
    }

}
