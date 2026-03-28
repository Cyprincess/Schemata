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

public class AdviceResultCacheShould
{
    [Fact]
    public async Task Advise_WithResult_StoresInCache() {
        var cache      = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
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
        var bytes = await cache.GetAsync(key);
        Assert.NotNull(bytes);
        var student = JsonSerializer.Deserialize<Student>(bytes);
        Assert.NotNull(student);
        Assert.Equal("Alice", student.FullName);
    }

    [Fact]
    public async Task Advise_NullResult_DoesNotStore() {
        var cache      = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var advisor    = new AdviceResultCache<Student, Student, Student>(cache);
        var ctx        = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var repository = new Mock<IRepository<Student>>().Object;
        var data       = new[] { new Student { Id = 1, FullName = "Alice" } }.AsQueryable();
        var context    = new QueryContext<Student, Student, Student>(repository, data) { Result = null };

        var result = await advisor.AdviseAsync(ctx, context, CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);

        var key = context.ToCacheKey();
        if (key != null) {
            Assert.Null(await cache.GetAsync(key));
        }
    }

    [Fact]
    public async Task Advise_Suppressed_DoesNotStore() {
        var cache   = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var advisor = new AdviceResultCache<Student, Student, Student>(cache);
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        ctx.Set(new QueryCacheSuppressed());
        var repository = new Mock<IRepository<Student>>().Object;
        var data       = new[] { new Student { Id = 1, FullName = "Alice" } }.AsQueryable();
        var context = new QueryContext<Student, Student, Student>(repository, data) {
            Result = new() { Id = 1, FullName = "Alice" },
        };

        var result = await advisor.AdviseAsync(ctx, context, CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);

        var key = context.ToCacheKey();
        if (key != null) {
            Assert.Null(await cache.GetAsync(key));
        }
    }
}
