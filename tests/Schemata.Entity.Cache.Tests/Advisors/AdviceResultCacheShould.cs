using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Caching.Skeleton;
using Schemata.Entity.Cache.Advisors;
using Schemata.Entity.Cache.Tests.Fixtures;
using Schemata.Entity.Repository;
using Xunit;

namespace Schemata.Entity.Cache.Tests.Advisors;

public class AdviceResultCacheShould
{
    private static IOptions<SchemataQueryCacheOptions> DefaultOptions() {
        return Options.Create(new SchemataQueryCacheOptions());
    }

    [Fact]
    public async Task Advise_WithResult_StoresInCache() {
        var mock = new Mock<ICacheProvider>();
        mock.Setup(x => x.SetAsync(
                       It.IsAny<string>(),
                       It.IsAny<byte[]>(),
                       It.IsAny<CacheEntryOptions>(),
                       It.IsAny<CancellationToken>()
                   )
             )
            .Returns(Task.CompletedTask);

        var advisor    = new AdviceResultCache<Student, Student, Student>(mock.Object, DefaultOptions());
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
        mock.Verify(
            x => x.SetAsync(key!, It.IsAny<byte[]>(), It.IsAny<CacheEntryOptions>(), It.IsAny<CancellationToken>()),
            Times.Once
        );
    }

    [Fact]
    public async Task Advise_NullResult_DoesNotStore() {
        var mock       = new Mock<ICacheProvider>();
        var advisor    = new AdviceResultCache<Student, Student, Student>(mock.Object, DefaultOptions());
        var ctx        = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var repository = new Mock<IRepository<Student>>().Object;
        var data       = new[] { new Student { Id = 1, FullName = "Alice" } }.AsQueryable();
        var context    = new QueryContext<Student, Student, Student>(repository, data) { Result = null };

        var result = await advisor.AdviseAsync(ctx, context, CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
        mock.Verify(
            x => x.SetAsync(
                It.IsAny<string>(),
                It.IsAny<byte[]>(),
                It.IsAny<CacheEntryOptions>(),
                It.IsAny<CancellationToken>()
            ),
            Times.Never
        );
    }

    [Fact]
    public async Task Advise_Suppressed_DoesNotStore() {
        var mock    = new Mock<ICacheProvider>();
        var advisor = new AdviceResultCache<Student, Student, Student>(mock.Object, DefaultOptions());
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        ctx.Set(new QueryCacheSuppressed());
        var repository = new Mock<IRepository<Student>>().Object;
        var data       = new[] { new Student { Id = 1, FullName = "Alice" } }.AsQueryable();
        var context = new QueryContext<Student, Student, Student>(repository, data) {
            Result = new() { Id = 1, FullName = "Alice" },
        };

        var result = await advisor.AdviseAsync(ctx, context, CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
        mock.Verify(
            x => x.SetAsync(
                It.IsAny<string>(),
                It.IsAny<byte[]>(),
                It.IsAny<CacheEntryOptions>(),
                It.IsAny<CancellationToken>()
            ),
            Times.Never
        );
    }

    [Fact]
    public async Task Advise_SingularResult_RegistersCacheKeyInCollection() {
        var mock = new Mock<ICacheProvider>();
        mock.Setup(x => x.SetAsync(
                       It.IsAny<string>(),
                       It.IsAny<byte[]>(),
                       It.IsAny<CacheEntryOptions>(),
                       It.IsAny<CancellationToken>()
                   )
             )
            .Returns(Task.CompletedTask);
        mock.Setup(x => x.CollectionAddAsync(
                       It.IsAny<string>(),
                       It.IsAny<string>(),
                       It.IsAny<CacheEntryOptions>(),
                       It.IsAny<CancellationToken>()
                   )
             )
            .Returns(Task.CompletedTask);

        var advisor    = new AdviceResultCache<Student, Student, Student>(mock.Object, DefaultOptions());
        var ctx        = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var repository = new Mock<IRepository<Student>>().Object;
        var data       = new[] { new Student { Id = 42, FullName = "Alice" } }.AsQueryable();
        var entity     = new Student { Id = 42, FullName = "Alice" };
        var context    = new QueryContext<Student, Student, Student>(repository, data) { Result = entity };

        var result = await advisor.AdviseAsync(ctx, context, CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
        var cacheKey = context.ToCacheKey();
        Assert.NotNull(cacheKey);
        var indexKey = ReverseIndex.BuildKey(typeof(Student), entity);
        Assert.NotNull(indexKey);
        mock.Verify(
            x => x.CollectionAddAsync(
                indexKey!,
                cacheKey,
                It.IsAny<CacheEntryOptions>(),
                It.IsAny<CancellationToken>()
            ),
            Times.Once
        );
    }

    [Fact]
    public async Task Advise_CollectionAggregateResult_SkipsCollection() {
        var mock = new Mock<ICacheProvider>();
        mock.Setup(x => x.SetAsync(
                       It.IsAny<string>(),
                       It.IsAny<byte[]>(),
                       It.IsAny<CacheEntryOptions>(),
                       It.IsAny<CancellationToken>()
                   )
             )
            .Returns(Task.CompletedTask);

        var advisor    = new AdviceResultCache<Student, Student, int>(mock.Object, DefaultOptions());
        var ctx        = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var repository = new Mock<IRepository<Student>>().Object;
        var data       = new[] { new Student { Id = 42 } }.AsQueryable();
        var context    = new QueryContext<Student, Student, int>(repository, data) { Result = 5 };

        var result = await advisor.AdviseAsync(ctx, context, CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
        mock.Verify(
            x => x.CollectionAddAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CacheEntryOptions>(),
                It.IsAny<CancellationToken>()
            ),
            Times.Never
        );
    }

    [Fact]
    public async Task Advise_ProjectionResultNotTEntity_DoesNotThrowAndSkipsCollection() {
        var mock = new Mock<ICacheProvider>();
        mock.Setup(x => x.SetAsync(
                       It.IsAny<string>(),
                       It.IsAny<byte[]>(),
                       It.IsAny<CacheEntryOptions>(),
                       It.IsAny<CancellationToken>()
                   )
             )
            .Returns(Task.CompletedTask);

        var advisor    = new AdviceResultCache<Student, StudentDto, StudentDto>(mock.Object, DefaultOptions());
        var ctx        = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var repository = new Mock<IRepository<Student>>().Object;
        var data = new[] { new Student { Id = 42, FullName = "Alice" } }.AsQueryable()
                                                                        .Select(s => new StudentDto(s.Id, s.FullName));
        var context = new QueryContext<Student, StudentDto, StudentDto>(repository, data) {
            Result = new(42, "Alice"),
        };

        var result = await advisor.AdviseAsync(ctx, context, CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
        mock.Verify(
            x => x.CollectionAddAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CacheEntryOptions>(),
                It.IsAny<CancellationToken>()
            ),
            Times.Never
        );
    }

    [Fact]
    public async Task Advise_ConfiguredTtl_AppliesToStoredEntry() {
        CacheEntryOptions? captured = null;
        var                mock     = new Mock<ICacheProvider>();
        mock.Setup(x => x.SetAsync(
                       It.IsAny<string>(),
                       It.IsAny<byte[]>(),
                       It.IsAny<CacheEntryOptions>(),
                       It.IsAny<CancellationToken>()
                   )
             )
            .Callback<string, byte[], CacheEntryOptions, CancellationToken>((
                                                                                _,
                                                                                _,
                                                                                opts,
                                                                                _
                                                                            ) => captured = opts
             )
            .Returns(Task.CompletedTask);

        var options    = Options.Create(new SchemataQueryCacheOptions { Ttl = TimeSpan.FromSeconds(7) });
        var advisor    = new AdviceResultCache<Student, Student, Student>(mock.Object, options);
        var ctx        = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var repository = new Mock<IRepository<Student>>().Object;
        var data       = new[] { new Student { Id = 1 } }.AsQueryable();
        var context = new QueryContext<Student, Student, Student>(repository, data) {
            Result = new() { Id = 1, FullName = "Alice" },
        };

        await advisor.AdviseAsync(ctx, context, CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal(TimeSpan.FromSeconds(7), captured!.SlidingExpiration);
    }
}
