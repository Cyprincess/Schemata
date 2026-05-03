using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Caching.Skeleton;
using Schemata.Entity.Cache.Advisors;
using Schemata.Entity.Cache.Tests.Fixtures;
using Schemata.Entity.Repository;
using Xunit;

namespace Schemata.Entity.Cache.Tests.Advisors;

public class AdviceUpdateEvictCacheShould
{
    private static IOptions<SchemataQueryCacheOptions> DefaultOptions() {
        return Options.Create(new SchemataQueryCacheOptions());
    }

    [Fact]
    public void Order_EqualsOrdersMax() {
        var mock    = new Mock<ICacheProvider>();
        var advisor = new AdviceUpdateEvictCache<Student>(mock.Object, DefaultOptions());
        Assert.Equal(SchemataConstants.Orders.Max, advisor.Order);
    }

    [Fact]
    public async Task AdviseAsync_WhenEntityUpdated_RemovesAllKeysInCollection() {
        var cacheKey1 = "first-key";
        var cacheKey2 = "second-key";
        var indexKey  = ReverseIndex.BuildKey(typeof(Student), new Student { Id = 7 });
        Assert.NotNull(indexKey);

        var mock = new Mock<ICacheProvider>();
        mock.Setup(x => x.CollectionMembersAsync(indexKey!, It.IsAny<CancellationToken>()))
            .ReturnsAsync([cacheKey1, cacheKey2]);

        var advisor = new AdviceUpdateEvictCache<Student>(mock.Object, DefaultOptions());
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var repo    = new Mock<IRepository<Student>>().Object;
        var entity  = new Student { Id = 7, FullName = "Alice" };

        var result = await advisor.AdviseAsync(ctx, repo, entity, CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
        mock.Verify(x => x.RemoveAsync(cacheKey1, It.IsAny<CancellationToken>()), Times.Once);
        mock.Verify(x => x.RemoveAsync(cacheKey2, It.IsAny<CancellationToken>()), Times.Once);
        mock.Verify(x => x.CollectionClearAsync(indexKey!, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AdviseAsync_WhenSuppressed_LeavesCacheIntact() {
        var mock    = new Mock<ICacheProvider>();
        var advisor = new AdviceUpdateEvictCache<Student>(mock.Object, DefaultOptions());
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        ctx.Set(new QueryCacheEvictionSuppressed());
        var repo   = new Mock<IRepository<Student>>().Object;
        var entity = new Student { Id = 11 };

        var result = await advisor.AdviseAsync(ctx, repo, entity, CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
        mock.Verify(x => x.CollectionMembersAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        mock.Verify(x => x.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        mock.Verify(x => x.CollectionClearAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AdviseAsync_WhenEvictionDisabled_LeavesCacheIntact() {
        var mock    = new Mock<ICacheProvider>();
        var options = Options.Create(new SchemataQueryCacheOptions { EvictionEnabled = false });
        var advisor = new AdviceUpdateEvictCache<Student>(mock.Object, options);
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var repo    = new Mock<IRepository<Student>>().Object;
        var entity  = new Student { Id = 11 };

        var result = await advisor.AdviseAsync(ctx, repo, entity, CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
        mock.Verify(x => x.CollectionMembersAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        mock.Verify(x => x.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        mock.Verify(x => x.CollectionClearAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AdviseAsync_WhenNoCollectionEntry_ReturnsContinueWithoutError() {
        var mock = new Mock<ICacheProvider>();
        mock.Setup(x => x.CollectionMembersAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var advisor = new AdviceUpdateEvictCache<Student>(mock.Object, DefaultOptions());
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var repo    = new Mock<IRepository<Student>>().Object;
        var entity  = new Student { Id = 999 };

        var result = await advisor.AdviseAsync(ctx, repo, entity, CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
    }

    [Fact]
    public async Task AdviseAsync_DoesNotEvictEntriesForDifferentEntityId() {
        var other      = new Student { Id = 100 };
        var otherIndex = ReverseIndex.BuildKey(typeof(Student), other);
        Assert.NotNull(otherIndex);

        var targetIndex = ReverseIndex.BuildKey(typeof(Student), new Student { Id = 1 });
        Assert.NotNull(targetIndex);

        var mock = new Mock<ICacheProvider>();
        mock.Setup(x => x.CollectionMembersAsync(targetIndex!, It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var advisor = new AdviceUpdateEvictCache<Student>(mock.Object, DefaultOptions());
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var repo    = new Mock<IRepository<Student>>().Object;

        var target = new Student { Id = 1 };
        var result = await advisor.AdviseAsync(ctx, repo, target, CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
        mock.Verify(x => x.CollectionMembersAsync(otherIndex!, It.IsAny<CancellationToken>()), Times.Never);
        mock.Verify(x => x.RemoveAsync(otherIndex!, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AdviseAsync_EndToEnd_EvictsResultStoredByResultCacheAdvisor() {
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

        var options = DefaultOptions();
        var write   = new AdviceResultCache<Student, Student, Student>(mock.Object, options);
        var evict   = new AdviceUpdateEvictCache<Student>(mock.Object, options);
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var repo    = new Mock<IRepository<Student>>().Object;
        var student = new Student { Id = 4242, FullName = "Zed" };
        var data    = new[] { student }.AsQueryable();
        var context = new QueryContext<Student, Student, Student>(repo, data) { Result = student };

        await write.AdviseAsync(ctx, context, CancellationToken.None);
        var cacheKey = context.ToCacheKey();
        Assert.NotNull(cacheKey);

        mock.Setup(x => x.CollectionMembersAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([cacheKey]);
        mock.Setup(x => x.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        mock.Setup(x => x.CollectionClearAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await evict.AdviseAsync(ctx, repo, student, CancellationToken.None);

        mock.Verify(x => x.RemoveAsync(cacheKey, It.IsAny<CancellationToken>()), Times.Once);
    }
}
