using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Caching.Skeleton;
using Schemata.Entity.Cache.Advisors;
using Schemata.Entity.Cache.Tests.Fixtures;
using Schemata.Entity.Repository;
using Xunit;

namespace Schemata.Entity.Cache.Tests.Advisors;

public class AdviceQueryCacheShould
{
    [Fact]
    public async Task Advise_CacheHit_ReturnsCachedResultAndHandle() {
        var cached = new Student { Uid = Guid.NewGuid(), FullName = "Cached Alice" };
        var mock   = new Mock<ICacheProvider>();
        mock.Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.SerializeToUtf8Bytes(cached));

        var advisor    = new AdviceQueryCache<Student, Student, Student>(mock.Object);
        var ctx        = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var repository = new Mock<IRepository<Student>>().Object;
        var data       = new[] { new Student { Uid = Guid.NewGuid(), FullName = "Alice" } }.AsQueryable();
        var context    = new QueryContext<Student, Student, Student>(repository, data);

        var result = await advisor.AdviseAsync(ctx, context, CancellationToken.None);

        Assert.Equal(AdviseResult.Handle, result);
        Assert.NotNull(context.Result);
        Assert.Equal("Cached Alice", context.Result!.FullName);
    }

    [Fact]
    public async Task Advise_CacheMiss_Continues() {
        var mock = new Mock<ICacheProvider>();
        mock.Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((byte[]?)null);

        var advisor    = new AdviceQueryCache<Student, Student, Student>(mock.Object);
        var ctx        = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var repository = new Mock<IRepository<Student>>().Object;
        var data       = new[] { new Student { Uid = Guid.NewGuid(), FullName = "Alice" } }.AsQueryable();
        var context    = new QueryContext<Student, Student, Student>(repository, data);

        var result = await advisor.AdviseAsync(ctx, context, CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
        Assert.Null(context.Result);
    }

    [Fact]
    public async Task Advise_Suppressed_Continues() {
        var mock    = new Mock<ICacheProvider>();
        var advisor = new AdviceQueryCache<Student, Student, Student>(mock.Object);
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        ctx.Set(new QueryCacheSuppressed());
        var repository = new Mock<IRepository<Student>>().Object;
        var data       = new[] { new Student { Uid = Guid.NewGuid(), FullName = "Alice" } }.AsQueryable();
        var context    = new QueryContext<Student, Student, Student>(repository, data);

        var result = await advisor.AdviseAsync(ctx, context, CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
        Assert.Null(context.Result);
        mock.Verify(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
