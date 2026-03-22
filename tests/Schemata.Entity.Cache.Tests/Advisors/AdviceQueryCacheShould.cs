using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
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
        var cache      = new MemoryCache(new MemoryCacheOptions());
        var advisor    = new AdviceQueryCache<Student, Student, Student>(cache);
        var ctx        = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var repository = new Mock<IRepository<Student>>().Object;
        var data       = new[] { new Student { Id = 1, FullName = "Alice" } }.AsQueryable();
        var context    = new QueryContext<Student, Student, Student>(repository, data);

        // Pre-populate cache with the key that context.ToCacheKey() will produce
        var key = context.ToCacheKey();
        Assert.NotNull(key);
        var cached = new Student { Id = 1, FullName = "Cached Alice" };
        cache.Set(key!, cached);

        var result = await advisor.AdviseAsync(ctx, context, CancellationToken.None);

        Assert.Equal(AdviseResult.Handle, result);
        Assert.NotNull(context.Result);
        Assert.Equal("Cached Alice", context.Result!.FullName);
    }

    [Fact]
    public async Task Advise_CacheMiss_Continues() {
        var cache      = new MemoryCache(new MemoryCacheOptions());
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
    public async Task Advise_Suppressed_Continues() {
        var cache   = new MemoryCache(new MemoryCacheOptions());
        var advisor = new AdviceQueryCache<Student, Student, Student>(cache);
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        ctx.Set(new SuppressQueryCache());
        var repository = new Mock<IRepository<Student>>().Object;
        var data       = new[] { new Student { Id = 1, FullName = "Alice" } }.AsQueryable();
        var context    = new QueryContext<Student, Student, Student>(repository, data);

        // Pre-populate cache
        var key = context.ToCacheKey();
        if (key != null) {
            cache.Set(key, new Student { Id = 1, FullName = "Cached" });
        }

        var result = await advisor.AdviseAsync(ctx, context, CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
        Assert.Null(context.Result);
    }
}
