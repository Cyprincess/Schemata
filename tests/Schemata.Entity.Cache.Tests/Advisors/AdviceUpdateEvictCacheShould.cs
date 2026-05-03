using System;
using System.Linq;
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

public class AdviceUpdateEvictCacheShould
{
    private static MemoryDistributedCache CreateCache() {
        return new(Options.Create(new MemoryDistributedCacheOptions()));
    }

    private static IOptions<SchemataQueryCacheOptions> DefaultOptions() {
        return Options.Create(new SchemataQueryCacheOptions());
    }

    [Fact]
    public void Order_EqualsOrdersMax() {
        var advisor = new AdviceUpdateEvictCache<Student>(CreateCache(), DefaultOptions());
        Assert.Equal(SchemataConstants.Orders.Max, advisor.Order);
    }

    [Fact]
    public async Task AdviseAsync_WhenEntityUpdated_RemovesAllKeysInReverseIndex() {
        var cache   = CreateCache();
        var advisor = new AdviceUpdateEvictCache<Student>(cache, DefaultOptions());
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var repo    = new Mock<IRepository<Student>>().Object;
        var entity  = new Student { Id = 7, FullName = "Alice" };

        // Seed reverse index and two matching cache entries for the entity.
        var cacheKey1 = "first-key";
        var cacheKey2 = "second-key";
        await cache.SetAsync(cacheKey1, JsonSerializer.SerializeToUtf8Bytes("payload-1"));
        await cache.SetAsync(cacheKey2, JsonSerializer.SerializeToUtf8Bytes("payload-2"));

        var indexKey = ReverseIndex.BuildKey(typeof(Student), entity);
        Assert.NotNull(indexKey);
        await ReverseIndex.WriteSetAsync(cache, indexKey, [cacheKey1, cacheKey2], TimeSpan.FromMinutes(5),
                                         CancellationToken.None);

        var result = await advisor.AdviseAsync(ctx, repo, entity, CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
        Assert.Null(await cache.GetAsync(cacheKey1));
        Assert.Null(await cache.GetAsync(cacheKey2));
        Assert.Null(await cache.GetAsync(indexKey));
    }

    [Fact]
    public async Task AdviseAsync_WhenSuppressed_LeavesCacheIntact() {
        var cache   = CreateCache();
        var advisor = new AdviceUpdateEvictCache<Student>(cache, DefaultOptions());
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
        var advisor = new AdviceUpdateEvictCache<Student>(cache, options);
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

    [Fact]
    public async Task AdviseAsync_WhenNoReverseIndexEntry_ReturnsContinueWithoutError() {
        var cache   = CreateCache();
        var advisor = new AdviceUpdateEvictCache<Student>(cache, DefaultOptions());
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var repo    = new Mock<IRepository<Student>>().Object;
        var entity  = new Student { Id = 999 };

        var result = await advisor.AdviseAsync(ctx, repo, entity, CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
    }

    [Fact]
    public async Task AdviseAsync_DoesNotEvictEntriesForDifferentEntityId() {
        var cache   = CreateCache();
        var advisor = new AdviceUpdateEvictCache<Student>(cache, DefaultOptions());
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var repo    = new Mock<IRepository<Student>>().Object;

        var other      = new Student { Id = 100 };
        var otherIndex = ReverseIndex.BuildKey(typeof(Student), other);
        Assert.NotNull(otherIndex);
        var otherCacheKey = "other-key";
        await cache.SetAsync(otherCacheKey, JsonSerializer.SerializeToUtf8Bytes("x"));
        await ReverseIndex.WriteSetAsync(cache, otherIndex, [otherCacheKey], TimeSpan.FromMinutes(5),
                                         CancellationToken.None);

        var target = new Student { Id = 1 };
        var result = await advisor.AdviseAsync(ctx, repo, target, CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
        Assert.NotNull(await cache.GetAsync(otherCacheKey));
        Assert.NotNull(await cache.GetAsync(otherIndex));
    }

    [Fact]
    public async Task AdviseAsync_EndToEnd_EvictsResultStoredByResultCacheAdvisor() {
        var cache   = CreateCache();
        var options = DefaultOptions();
        var write   = new AdviceResultCache<Student, Student, Student>(cache, options);
        var evict   = new AdviceUpdateEvictCache<Student>(cache, options);
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var repo    = new Mock<IRepository<Student>>().Object;
        var student = new Student { Id = 4242, FullName = "Zed" };
        var data    = new[] { student }.AsQueryable();
        var context = new QueryContext<Student, Student, Student>(repo, data) { Result = student };

        await write.AdviseAsync(ctx, context, CancellationToken.None);
        var cacheKey = context.ToCacheKey();
        Assert.NotNull(cacheKey);
        Assert.NotNull(await cache.GetAsync(cacheKey));

        await evict.AdviseAsync(ctx, repo, student, CancellationToken.None);

        Assert.Null(await cache.GetAsync(cacheKey));
    }
}
