using System;
using System.Linq;
using System.Linq.Expressions;
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
    private static IOptions<SchemataQueryCacheOptions> DefaultOptions() {
        return Options.Create(new SchemataQueryCacheOptions());
    }

    private static MemoryDistributedCache CreateCache() {
        return new(Options.Create(new MemoryDistributedCacheOptions()));
    }

    [Fact]
    public async Task Advise_WithResult_StoresInCache() {
        var cache      = CreateCache();
        var advisor    = new AdviceResultCache<Student, Student, Student>(cache, DefaultOptions());
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
        var cache      = CreateCache();
        var advisor    = new AdviceResultCache<Student, Student, Student>(cache, DefaultOptions());
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
        var cache   = CreateCache();
        var advisor = new AdviceResultCache<Student, Student, Student>(cache, DefaultOptions());
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

    [Fact]
    public async Task Advise_SingularResult_RegistersCacheKeyInReverseIndex() {
        var cache      = CreateCache();
        var advisor    = new AdviceResultCache<Student, Student, Student>(cache, DefaultOptions());
        var ctx        = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var repository = new Mock<IRepository<Student>>().Object;
        var data       = new[] { new Student { Id = 42, FullName = "Alice" } }.AsQueryable();
        var entity     = new Student { Id = 42, FullName = "Alice" };
        var context    = new QueryContext<Student, Student, Student>(repository, data) { Result = entity };

        await advisor.AdviseAsync(ctx, context, CancellationToken.None);

        var cacheKey = context.ToCacheKey();
        Assert.NotNull(cacheKey);

        var indexKey = ReverseIndex.BuildKey(typeof(Student), entity);
        Assert.NotNull(indexKey);

        var indexed = await ReverseIndex.ReadSetAsync(cache, indexKey, CancellationToken.None);
        Assert.Contains(cacheKey, indexed);
    }

    [Fact]
    public async Task Advise_CollectionAggregateResult_SkipsReverseIndex() {
        var cache      = CreateCache();
        var advisor    = new AdviceResultCache<Student, Student, int>(cache, DefaultOptions());
        var ctx        = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var repository = new Mock<IRepository<Student>>().Object;
        var data       = new[] { new Student { Id = 42 } }.AsQueryable();
        var context    = new QueryContext<Student, Student, int>(repository, data) { Result = 5 };

        await advisor.AdviseAsync(ctx, context, CancellationToken.None);

        var probe    = new Student { Id = 42 };
        var indexKey = ReverseIndex.BuildKey(typeof(Student), probe);
        Assert.NotNull(indexKey);

        var indexed = await ReverseIndex.ReadSetAsync(cache, indexKey, CancellationToken.None);
        Assert.Empty(indexed);
    }

    [Fact]
    public async Task Advise_ProjectionResultNotTEntity_DoesNotThrowAndSkipsReverseIndex() {
        var cache      = CreateCache();
        var advisor    = new AdviceResultCache<Student, StudentDto, StudentDto>(cache, DefaultOptions());
        var ctx        = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var repository = new Mock<IRepository<Student>>().Object;
        var data = new[] { new Student { Id = 42, FullName = "Alice" } }.AsQueryable()
                                                                        .Select(s => new StudentDto(s.Id, s.FullName));
        var context = new QueryContext<Student, StudentDto, StudentDto>(repository, data) {
            Result = new(42, "Alice"),
        };

        var result = await advisor.AdviseAsync(ctx, context, CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);

        var probe    = new Student { Id = 42 };
        var indexKey = ReverseIndex.BuildKey(typeof(Student), probe);
        Assert.NotNull(indexKey);
        var indexed = await ReverseIndex.ReadSetAsync(cache, indexKey, CancellationToken.None);
        Assert.Empty(indexed);
    }

    [Fact]
    public async Task Advise_ConfiguredTtl_AppliesToStoredEntry() {
        var cache      = CreateCache();
        var options    = Options.Create(new SchemataQueryCacheOptions { Ttl = TimeSpan.FromSeconds(7) });
        var advisor    = new AdviceResultCache<Student, Student, Student>(cache, options);
        var ctx        = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var repository = new Mock<IRepository<Student>>().Object;
        var data       = new[] { new Student { Id = 1 } }.AsQueryable();
        var context = new QueryContext<Student, Student, Student>(repository, data) {
            Result = new() { Id = 1, FullName = "Alice" },
        };

        // Use a spy wrapper to inspect the options passed to SetAsync.
        var captured   = new CapturedOptionsCache(cache);
        var spyAdvisor = new AdviceResultCache<Student, Student, Student>(captured, options);

        await spyAdvisor.AdviseAsync(ctx, context, CancellationToken.None);

        Assert.NotNull(captured.LastOptions);
        Assert.Equal(TimeSpan.FromSeconds(7), captured.LastOptions!.SlidingExpiration);
    }

    [Fact]
    public async Task Advise_DuplicateCacheKey_RefreshesIndexWithoutRewritingSet() {
        var inner      = CreateCache();
        var cache      = new SpyDistributedCache(inner);
        var advisor    = new AdviceResultCache<Student, Student, Student>(cache, DefaultOptions());
        var ctx        = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var repository = new Mock<IRepository<Student>>().Object;
        var data       = new[] { new Student { Id = 42 } }.AsQueryable();
        var entity     = new Student { Id = 42, FullName = "Alice" };
        var context    = new QueryContext<Student, Student, Student>(repository, data) { Result = entity };

        await advisor.AdviseAsync(ctx, context, CancellationToken.None);
        var indexKey = ReverseIndex.BuildKey(typeof(Student), entity);
        Assert.NotNull(indexKey);
        Assert.Equal(1, cache.SetCount(indexKey!));
        Assert.Equal(0, cache.RefreshCount(indexKey!));

        await advisor.AdviseAsync(ctx, context, CancellationToken.None);
        Assert.Equal(1, cache.SetCount(indexKey!));
        Assert.Equal(1, cache.RefreshCount(indexKey!));
    }

    [Fact]
    public async Task Advise_ConcurrentWritesForSameEntity_PreservesAllCacheKeys() {
        var inner    = CreateCache();
        var cache    = new SpyDistributedCache(inner);
        var options  = DefaultOptions();
        var probe    = new Student { Id = 100 };
        var indexKey = ReverseIndex.BuildKey(typeof(Student), probe)!;

        cache.OnBeforeSet = key => key == indexKey ? Task.Delay(30) : Task.CompletedTask;

        var repository = new Mock<IRepository<Student>>().Object;

        Task RunOne(Expression<Func<Student, bool>> filter) {
            return Task.Run(async () => {
                var advisor = new AdviceResultCache<Student, Student, Student>(cache, options);
                var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
                var data    = new[] { new Student { Id = 100 } }.AsQueryable().Where(filter);
                var context = new QueryContext<Student, Student, Student>(repository, data) {
                    Result = new() { Id = 100 },
                };
                await advisor.AdviseAsync(ctx, context, CancellationToken.None);
            });
        }

        await Task.WhenAll(RunOne(s => s.Age > 10), RunOne(s => s.Age > 20), RunOne(s => s.Age > 30),
                           RunOne(s => s.Age > 40), RunOne(s => s.Age > 50));

        var indexed = await ReverseIndex.ReadSetAsync(inner, indexKey, CancellationToken.None);
        Assert.Equal(5, indexed.Count);
    }

    #region Nested type: CapturedOptionsCache

    private sealed class CapturedOptionsCache : IDistributedCache
    {
        private readonly IDistributedCache _inner;

        public CapturedOptionsCache(IDistributedCache inner) { _inner = inner; }

        public DistributedCacheEntryOptions? LastOptions { get; private set; }

        #region IDistributedCache Members

        public byte[]? Get(string key) { return _inner.Get(key); }

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) {
            return _inner.GetAsync(key, token);
        }

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) {
            LastOptions = options;
            _inner.Set(key, value, options);
        }

        public Task SetAsync(
            string                       key,
            byte[]                       value,
            DistributedCacheEntryOptions options,
            CancellationToken            token = default
        ) {
            LastOptions = options;
            return _inner.SetAsync(key, value, options, token);
        }

        public void Refresh(string key) { _inner.Refresh(key); }

        public Task RefreshAsync(string key, CancellationToken token = default) {
            return _inner.RefreshAsync(key, token);
        }

        public void Remove(string key) { _inner.Remove(key); }

        public Task RemoveAsync(string key, CancellationToken token = default) {
            return _inner.RemoveAsync(key, token);
        }

        #endregion
    }

    #endregion
}
