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

public class AdviceResultCacheShould
{
    [Fact]
    public async Task Advise_WithResult_StoresInCache() {
        var cache      = new MemoryCache(new MemoryCacheOptions());
        var advisor    = new AdviceResultCache<Student, Student, Student>(cache);
        var ctx        = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var repository = new Mock<IRepository<Student>>().Object;
        var data       = new[] { new Student { Id = 1, FullName = "Alice" } }.AsQueryable();
        var context = new QueryContext<Student, Student, Student>(repository, data) {
            Result = new() { Id = 1, FullName = "Alice" },
        };

        var result = await advisor.AdviseAsync(ctx, context, CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);

        var key = context.ToCacheKey();
        Assert.NotNull(key);
        Assert.True(cache.TryGetValue(key, out var stored));
        Assert.NotNull(stored);
        var student = Assert.IsType<Student>(stored);
        Assert.Equal("Alice", student.FullName);
    }

    [Fact]
    public async Task Advise_NullResult_DoesNotStore() {
        var cache      = new MemoryCache(new MemoryCacheOptions());
        var advisor    = new AdviceResultCache<Student, Student, Student>(cache);
        var ctx        = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var repository = new Mock<IRepository<Student>>().Object;
        var data       = new[] { new Student { Id = 1, FullName = "Alice" } }.AsQueryable();
        var context    = new QueryContext<Student, Student, Student>(repository, data) { Result = null };

        var result = await advisor.AdviseAsync(ctx, context, CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);

        var key = context.ToCacheKey();
        if (key != null) {
            Assert.False(cache.TryGetValue(key, out var _));
        }
    }

    [Fact]
    public async Task Advise_Suppressed_DoesNotStore() {
        var cache   = new MemoryCache(new MemoryCacheOptions());
        var advisor = new AdviceResultCache<Student, Student, Student>(cache);
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        ctx.Set(new SuppressQueryCache());
        var repository = new Mock<IRepository<Student>>().Object;
        var data       = new[] { new Student { Id = 1, FullName = "Alice" } }.AsQueryable();
        var context = new QueryContext<Student, Student, Student>(repository, data) {
            Result = new() { Id = 1, FullName = "Alice" },
        };

        var result = await advisor.AdviseAsync(ctx, context, CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);

        var key = context.ToCacheKey();
        if (key != null) {
            Assert.False(cache.TryGetValue(key, out var _));
        }
    }
}
