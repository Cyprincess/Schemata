using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Caching.Skeleton;
using Schemata.Entity.Cache.Tests.Fixtures;
using Schemata.Entity.Repository;
using Xunit;

namespace Schemata.Entity.Cache.Tests.Advisors;

[Trait("Layer", "Unit")]
public class AdviceQueryCacheShould
{
    [Fact]
    public async Task Read_AfterFill_ReturnsSerializedResult() {
        using var cache = new QueryCacheTestContext();
        var first = cache.Query<Student>(QueryOperation.FirstOrDefault);
        Assert.Equal(AdviseResult.Continue, await cache.Read(first));
        var student = new Student { Uid = Guid.NewGuid(), FullName = "Alice" };
        await cache.Fill(first, student);
        student.FullName = "Changed after fill";

        var second = cache.Query<Student>(QueryOperation.FirstOrDefault);
        Assert.Equal(AdviseResult.Handle, await cache.Read(second));
        Assert.Equal("Alice", second.Result!.FullName);
        Assert.Equal(student.Uid, second.Result.Uid);
    }

    [Fact]
    public async Task Read_AfterGenerationMetadataLoss_DoesNotResurrectOldResult() {
        using var cache = new QueryCacheTestContext();
        string? generationKey = null;
        cache.Cache.Setup(x => x.TryAddAsync(It.IsAny<string>(), It.IsAny<byte[]>(),
                It.IsAny<CacheEntryOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, byte[] value, CacheEntryOptions options, CancellationToken _) => {
                generationKey = key;
                cache.Store(key, value, options);
                return true;
            });
        var old = cache.Query<int>(QueryOperation.Count);
        await cache.Read(old);
        await cache.Fill(old, 7);
        cache.Remove(generationKey!);

        var current = cache.Query<int>(QueryOperation.Count);
        Assert.Equal(AdviseResult.Continue, await cache.Read(current));
        await cache.Fill(current, 9);
        var next = cache.Query<int>(QueryOperation.Count);
        Assert.Equal(AdviseResult.Handle, await cache.Read(next));
        Assert.Equal(9, next.Result);
    }

    [Fact]
    public async Task Read_WhenWeakInitializationOverlapsCommit_DoesNotRestoreOldResult() {
        using var cache = new QueryCacheTestContext();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        cache.Cache.Setup(x => x.TryAddAsync(It.IsAny<string>(), It.IsAny<byte[]>(),
                It.IsAny<CacheEntryOptions>(), It.IsAny<CancellationToken>()))
            .Returns(async (string key, byte[] value, CacheEntryOptions options, CancellationToken _) => {
                if (Interlocked.Increment(ref attempts) == 1) {
                    entered.SetResult();
                    await release.Task;
                }
                cache.Store(key, value, options);
                return true;
            });

        var delayed = cache.Query<int>(QueryOperation.Count);
        var initialization = cache.Read(delayed);
        try {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var old = cache.Query<int>(QueryOperation.Count);
            await cache.Read(old);
            await cache.Fill(old, 7);
            await cache.Commit(new() { Updated = [new Student()] });
        } finally {
            release.TrySetResult();
        }

        Assert.Equal(AdviseResult.Continue, await initialization);
        await cache.Fill(delayed, 9);
        var next = cache.Query<int>(QueryOperation.Count);
        Assert.Equal(AdviseResult.Handle, await cache.Read(next));
        Assert.Equal(9, next.Result);
        await cache.Commit(new() { Updated = [new Student()] });
        Assert.Equal(AdviseResult.Continue, await cache.Read(cache.Query<int>(QueryOperation.Count)));
    }

    [Fact]
    public async Task Read_WhenProviderCannotKeyQuery_SkipsReadAndFill() {
        using var cache = new QueryCacheTestContext();
        cache.UseUnsupportedProvider();
        var query = cache.Query<int>(QueryOperation.Count);
        Assert.Equal(AdviseResult.Continue, await cache.Read(query));
        await cache.Fill(query, 7);
        Assert.Equal(AdviseResult.Continue, await cache.Read(cache.Query<int>(QueryOperation.Count)));
    }

    [Fact]
    public async Task Read_WhenRepositoryHasNoKeyProvider_SkipsReadAndFill() {
        using var cache = new QueryCacheTestContext();
        var bare = new Mock<IRepository<Student>>();
        var query = new QueryContext<Student, int, int>(bare.Object, QueryOperation.Count, Array.Empty<Student>().AsQueryable().Select(s => s.Age));
        Assert.Equal(AdviseResult.Continue, await cache.Read(query));
        await cache.Fill(query, 7);
        Assert.Equal(AdviseResult.Continue, await cache.Read(query));
    }

    [Fact]
    public async Task Read_WhenProviderKeyDiffers_DoesNotServeOtherStore() {
        using var cache = new QueryCacheTestContext();
        cache.UseProviderKey("store-a");
        var filled = cache.Query<int>(QueryOperation.Count);
        await cache.Read(filled);
        await cache.Fill(filled, 7);

        cache.UseProviderKey("store-b");
        Assert.Equal(AdviseResult.Continue, await cache.Read(cache.Query<int>(QueryOperation.Count)));

        cache.UseProviderKey("store-a");
        var hit = cache.Query<int>(QueryOperation.Count);
        Assert.Equal(AdviseResult.Handle, await cache.Read(hit));
        Assert.Equal(7, hit.Result);
    }

    [Fact]
    public async Task Read_WhenOperationDiffers_DoesNotServeFirstResultAsSingle() {
        using var cache = new QueryCacheTestContext();
        var first = cache.Query<Student>(QueryOperation.FirstOrDefault);
        await cache.Read(first);
        await cache.Fill(first, new Student { FullName = "Alice" });

        Assert.Equal(AdviseResult.Continue, await cache.Read(cache.Query<Student>(QueryOperation.SingleOrDefault)));
        Assert.Equal(AdviseResult.Handle, await cache.Read(cache.Query<Student>(QueryOperation.FirstOrDefault)));
    }

    [Fact]
    public async Task Read_WhenClientProjectionDiffers_DoesNotShareEntry() {
        using var cache = new QueryCacheTestContext();
        var data = Array.Empty<Student>().AsQueryable();
        var first = new QueryContext<Student, Student, Student>(cache.Repository, QueryOperation.FirstOrDefault,
            data.Select(row => new Student { FullName = row.FullName + "first" }));
        await cache.Read(first);
        await cache.Fill(first, new Student { FullName = "Alicefirst" });

        var second = new QueryContext<Student, Student, Student>(cache.Repository, QueryOperation.FirstOrDefault,
            data.Select(row => new Student { FullName = row.FullName + "second" }));
        Assert.Equal(AdviseResult.Continue, await cache.Read(second));
        Assert.Equal(AdviseResult.Handle, await cache.Read(first));
        Assert.Equal("Alicefirst", first.Result!.FullName);
    }
}
