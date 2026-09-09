using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Caching.Skeleton;
using Schemata.Entity.Cache.Advisors;
using Schemata.Entity.Cache.Tests.Fixtures;
using Schemata.Entity.Repository;
using Xunit;

namespace Schemata.Entity.Cache.Tests.Advisors;

[Trait("Layer", "Unit")]
public class AdviceCommittedEvictCacheShould
{
    [Fact]
    public async Task Commit_WithUpdate_LatePrecommitFillCannotRepopulateCurrentGeneration() {
        using var cache = new QueryCacheTestContext();
        var captured = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var old = cache.Query<int>();
        async Task FinishOldRead() {
            await cache.Read(old);
            captured.SetResult();
            await release.Task;
            await cache.Fill(old, 7);
        }
        var pending = FinishOldRead();
        try {
            await captured.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await cache.Commit(new() { Updated = [new Student()] });
            var current = cache.Query<int>();
            Assert.Equal(AdviseResult.Continue, await cache.Read(current));
            await cache.Fill(current, 9);
        } finally {
            release.TrySetResult();
        }
        await pending;

        var next = cache.Query<int>();
        Assert.Equal(AdviseResult.Handle, await cache.Read(next));
        Assert.Equal(9, next.Result);
        await cache.Commit(new() { Updated = [new Student()] });
        Assert.Equal(AdviseResult.Continue, await cache.Read(cache.Query<int>()));
    }

    [Fact]
    public async Task Commit_WithAddedEntity_InvalidatesCountAndProjection() {
        using var cache = new QueryCacheTestContext();
        var count = cache.Query<int>();
        var data = Array.Empty<Student>().AsQueryable().Select(s => new StudentDto(s.Uid, s.FullName));
        var projection = new QueryContext<Student, StudentDto, StudentDto>(cache.Repository, data);
        await cache.Read(count);
        await cache.Fill(count, 7);
        await cache.Read(projection);
        await cache.Fill(projection, new StudentDto(Guid.NewGuid(), "Alice"));
        Assert.Equal(AdviseResult.Handle, await cache.Read(cache.Query<int>()));
        Assert.Equal(AdviseResult.Handle,
            await cache.Read(new QueryContext<Student, StudentDto, StudentDto>(cache.Repository, data)));

        await cache.Commit(new() { Added = [new Student()] });

        Assert.Equal(AdviseResult.Continue, await cache.Read(cache.Query<int>()));
        Assert.Equal(AdviseResult.Continue,
            await cache.Read(new QueryContext<Student, StudentDto, StudentDto>(cache.Repository, data)));
    }

    [Fact]
    public async Task Commit_WithRemovedEntity_InvalidatesPreviouslyCachedResult() {
        using var cache = new QueryCacheTestContext();
        var query = cache.Query<Student>();
        await cache.Read(query);
        await cache.Fill(query, new Student { FullName = "Alice" });
        await cache.Commit(new() { Removed = [new Student()] });
        Assert.Equal(AdviseResult.Continue, await cache.Read(cache.Query<Student>()));
    }

    [Fact]
    public async Task Commit_BetweenNestedQueries_PreservesEachQuerySnapshot() {
        using var cache = new QueryCacheTestContext();
        var outer = cache.Query<int>();
        await cache.Read(outer);
        await cache.Commit(new() { Updated = [new Student()] });
        var inner = cache.Query<int>();
        await cache.Read(inner);
        await cache.Fill(inner, 9);
        await cache.Fill(outer, 7);

        var current = cache.Query<int>();
        Assert.Equal(AdviseResult.Handle, await cache.Read(current));
        Assert.Equal(9, current.Result);
    }

    [Fact]
    public async Task Commit_WithConcurrentInvalidations_DoesNotReuseIntermediateGeneration() {
        using var cache = new QueryCacheTestContext();
        var old = cache.Query<int>();
        await cache.Read(old);
        await cache.Fill(old, 7);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writes = 0;
        cache.Cache.Setup(x => x.SetAsync(It.IsAny<string>(), It.IsAny<byte[]>(),
                It.IsAny<CacheEntryOptions>(), It.IsAny<CancellationToken>()))
            .Returns(async (string key, byte[] value, CacheEntryOptions options, CancellationToken _) => {
                if (Interlocked.Increment(ref writes) == 1) {
                    entered.SetResult();
                    await release.Task;
                }
                cache.Store(key, value, options);
            });
        var first = cache.Commit(new() { Updated = [new Student()] });
        try {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await cache.Commit(new() { Updated = [new Student()] });
            var intermediate = cache.Query<int>();
            await cache.Read(intermediate);
            await cache.Fill(intermediate, 9);
        } finally {
            release.TrySetResult();
        }
        await first;
        Assert.Equal(AdviseResult.Continue, await cache.Read(cache.Query<int>()));
    }

    [Theory]
    [InlineData("NoChanges")]
    [InlineData("EvictionSuppressed")]
    [InlineData("EvictionDisabled")]
    public async Task Commit_WhenEvictionDoesNotApply_PreservesCachedResult(string condition) {
        using var cache = new QueryCacheTestContext();
        var query = cache.Query<int>();
        await cache.Read(query);
        await cache.Fill(query, 7);
        cache.Options.EvictionEnabled = condition != "EvictionDisabled";
        if (condition == "EvictionSuppressed") {
            cache.Advice.Set(new QueryCacheEvictionSuppressed());
        }
        await cache.Commit(condition == "NoChanges" ? new() : new() { Updated = [new Student()] });

        var next = cache.Query<int>();
        Assert.Equal(AdviseResult.Handle, await cache.Read(next));
        Assert.Equal(7, next.Result);
    }

    [Fact]
    public async Task Commit_WhenQueryCacheSuppressed_StillInvalidatesCachedResult() {
        using var cache = new QueryCacheTestContext();
        var query = cache.Query<int>();
        await cache.Read(query);
        await cache.Fill(query, 7);
        using (cache.Advice.Use<QueryCacheSuppressed>()) {
            await cache.Commit(new() { Updated = [new Student()] });
        }
        Assert.Equal(AdviseResult.Continue, await cache.Read(cache.Query<int>()));
    }
}
