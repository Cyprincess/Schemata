using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Entity.Cache.Advisors;
using Schemata.Entity.Cache.Tests.Fixtures;
using Schemata.Entity.Repository;
using Xunit;

namespace Schemata.Entity.Cache.Tests.Advisors;

public class AdviceRemoveEvictCacheShould
{
    private static MemoryDistributedCache CreateCache() {
        return new(Options.Create(new MemoryDistributedCacheOptions()));
    }

    private static IOptions<SchemataQueryCacheOptions> DefaultOptions() {
        return Options.Create(new SchemataQueryCacheOptions());
    }

    [Fact]
    public void Order_EqualsOrdersMax() {
        var advisor = new AdviceRemoveEvictCache<Student>(CreateCache(), DefaultOptions());
        Assert.Equal(SchemataConstants.Orders.Max, advisor.Order);
    }

    [Fact]
    public async Task AdviseAsync_WhenEntityRemoved_RemovesAllKeysInReverseIndex() {
        var cache   = CreateCache();
        var advisor = new AdviceRemoveEvictCache<Student>(cache, DefaultOptions());
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var repo    = new Mock<IRepository<Student>>().Object;
        var entity  = new Student { Id = 9 };

        var cacheKey = "remove-key";
        await cache.SetAsync(cacheKey, JsonSerializer.SerializeToUtf8Bytes("payload"));
        var indexKey = ReverseIndex.BuildKey(typeof(Student), entity);
        Assert.NotNull(indexKey);
        await ReverseIndex.WriteSetAsync(cache, indexKey, [cacheKey], TimeSpan.FromMinutes(5), CancellationToken.None);

        var result = await advisor.AdviseAsync(ctx, repo, entity, CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
        Assert.Null(await cache.GetAsync(cacheKey));
        Assert.Null(await cache.GetAsync(indexKey));
    }

    [Fact]
    public async Task AdviseAsync_WhenSuppressed_LeavesCacheIntact() {
        var cache   = CreateCache();
        var advisor = new AdviceRemoveEvictCache<Student>(cache, DefaultOptions());
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        ctx.Set(new QueryCacheEvictionSuppressed());
        var repo   = new Mock<IRepository<Student>>().Object;
        var entity = new Student { Id = 11 };

        var cacheKey = "k";
        await cache.SetAsync(cacheKey, JsonSerializer.SerializeToUtf8Bytes("v"));
        var indexKey = ReverseIndex.BuildKey(typeof(Student), entity);
        Assert.NotNull(indexKey);
        await ReverseIndex.WriteSetAsync(cache, indexKey, [cacheKey], TimeSpan.FromMinutes(5), CancellationToken.None);

        var result = await advisor.AdviseAsync(ctx, repo, entity, CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
        Assert.NotNull(await cache.GetAsync(cacheKey));
        Assert.NotNull(await cache.GetAsync(indexKey));
    }

    [Fact]
    public async Task AdviseAsync_WhenEvictionDisabled_LeavesCacheIntact() {
        var cache   = CreateCache();
        var options = Options.Create(new SchemataQueryCacheOptions { EvictionEnabled = false });
        var advisor = new AdviceRemoveEvictCache<Student>(cache, options);
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var repo    = new Mock<IRepository<Student>>().Object;
        var entity  = new Student { Id = 11 };

        var cacheKey = "k";
        await cache.SetAsync(cacheKey, JsonSerializer.SerializeToUtf8Bytes("v"));
        var indexKey = ReverseIndex.BuildKey(typeof(Student), entity);
        Assert.NotNull(indexKey);
        await ReverseIndex.WriteSetAsync(cache, indexKey, [cacheKey], TimeSpan.FromMinutes(5), CancellationToken.None);

        var result = await advisor.AdviseAsync(ctx, repo, entity, CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
        Assert.NotNull(await cache.GetAsync(cacheKey));
        Assert.NotNull(await cache.GetAsync(indexKey));
    }
}
