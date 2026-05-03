using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Entity.Cache.Advisors;
using Schemata.Entity.Cache.Tests.Fixtures;
using Schemata.Entity.Repository;
using Xunit;

namespace Schemata.Entity.Cache.Tests.Advisors;

public class AdviceQueryCacheShould
{
    [Fact]
    public async Task Advise_CacheHit_ReturnsCachedResultAndHandle() {
        var cache      = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var advisor    = new AdviceQueryCache<Student, Student, Student>(cache);
        var ctx        = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var repository = new Mock<IRepository<Student>>().Object;
        var data       = new[] { new Student { Id = 1, FullName = "Alice" } }.AsQueryable();
        var context    = new QueryContext<Student, Student, Student>(repository, data);

        // Pre-populate cache with the key that context.ToCacheKey() will produce
        var key = context.ToCacheKey();
        Assert.NotNull(key);
        var cached = new Student { Id = 1, FullName = "Cached Alice" };
        await cache.SetAsync(key, JsonSerializer.SerializeToUtf8Bytes(cached));

        var result = await advisor.AdviseAsync(ctx, context, CancellationToken.None);

        Assert.Equal(AdviseResult.Handle, result);
        Assert.NotNull(context.Result);
        Assert.Equal("Cached Alice", context.Result!.FullName);
    }

    [Fact]
    public async Task Advise_CacheMiss_Continues() {
        var cache      = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var advisor    = new AdviceQueryCache<Student, Student, Student>(cache);
        var ctx        = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var repository = new Mock<IRepository<Student>>().Object;
        var data       = new[] { new Student { Id = 1, FullName = "Alice" } }.AsQueryable();
        var context    = new QueryContext<Student, Student, Student>(repository, data);

        var result = await advisor.AdviseAsync(ctx, context, CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
        Assert.Null(context.Result);
    }

    [Fact]
    public async Task Advise_SingularCacheHit_RefreshesReverseIndexTtl() {
        var inner      = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var cache      = new SpyDistributedCache(inner);
        var advisor    = new AdviceQueryCache<Student, Student, Student>(cache);
        var ctx        = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var repository = new Mock<IRepository<Student>>().Object;
        var data       = new[] { new Student { Id = 1, FullName = "Alice" } }.AsQueryable();
        var context    = new QueryContext<Student, Student, Student>(repository, data);

        var cacheKey = context.ToCacheKey();
        Assert.NotNull(cacheKey);
        var cached = new Student { Id = 7, FullName = "Cached" };
        await inner.SetAsync(cacheKey, JsonSerializer.SerializeToUtf8Bytes(cached));

        var result = await advisor.AdviseAsync(ctx, context, CancellationToken.None);

        Assert.Equal(AdviseResult.Handle, result);

        var probe    = new Student { Id = 7 };
        var indexKey = ReverseIndex.BuildKey(typeof(Student), probe);
        Assert.NotNull(indexKey);
        Assert.Equal(1, cache.RefreshCount(indexKey!));
    }

    [Fact]
    public async Task Advise_Suppressed_Continues() {
        var cache   = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var advisor = new AdviceQueryCache<Student, Student, Student>(cache);
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        ctx.Set(new QueryCacheSuppressed());
        var repository = new Mock<IRepository<Student>>().Object;
        var data       = new[] { new Student { Id = 1, FullName = "Alice" } }.AsQueryable();
        var context    = new QueryContext<Student, Student, Student>(repository, data);

        // Pre-populate cache
        var key = context.ToCacheKey();
        if (key != null) {
            await cache.SetAsync(key, JsonSerializer.SerializeToUtf8Bytes(new Student { Id = 1, FullName = "Cached" }));
        }

        var result = await advisor.AdviseAsync(ctx, context, CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
        Assert.Null(context.Result);
    }
}
