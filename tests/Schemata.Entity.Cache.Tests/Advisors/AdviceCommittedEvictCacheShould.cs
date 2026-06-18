using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions;
using Schemata.Abstractions.Advisors;
using Schemata.Caching.Skeleton;
using Schemata.Common;
using Schemata.Entity.Cache.Advisors;
using Schemata.Entity.Cache.Tests.Fixtures;
using Schemata.Entity.Repository;
using Xunit;

namespace Schemata.Entity.Cache.Tests.Advisors;

public class AdviceCommittedEvictCacheShould
{
    private static IOptions<SchemataQueryCacheOptions> DefaultOptions() {
        return Options.Create(new SchemataQueryCacheOptions());
    }

    [Fact]
    public void Order_EqualsOrdersMax() {
        var mock    = new Mock<ICacheProvider>();
        var advisor = new AdviceCommittedEvictCache<Student>(mock.Object, DefaultOptions());
        Assert.Equal(SchemataConstants.Orders.Max, advisor.Order);
    }

    [Fact]
    public async Task AdviseAsync_WhenEntitiesUpdated_RemovesAllKeysInCollection() {
        var cacheKey1 = "first-key";
        var cacheKey2 = "second-key";
        var uid       = Identifiers.NewUid();
        var indexKey  = ReverseIndex.BuildKey(typeof(Student), new Student { Uid = uid });
        Assert.NotNull(indexKey);

        var mock = new Mock<ICacheProvider>();
        mock.Setup(x => x.CollectionMembersAsync(indexKey!, It.IsAny<CancellationToken>()))
            .ReturnsAsync([cacheKey1, cacheKey2]);

        var advisor = new AdviceCommittedEvictCache<Student>(mock.Object, DefaultOptions());
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var repo    = Mock.Of<IRepository<Student>>();
        var changes = new CommitChanges<Student> {
            Updated = [new() { Uid = uid, FullName = "Alice" }],
        };

        var result = await advisor.AdviseAsync(ctx, repo, changes, CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
        mock.Verify(x => x.RemoveAsync(cacheKey1, It.IsAny<CancellationToken>()), Times.Once);
        mock.Verify(x => x.RemoveAsync(cacheKey2, It.IsAny<CancellationToken>()), Times.Once);
        mock.Verify(x => x.CollectionClearAsync(indexKey!, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AdviseAsync_WhenEntitiesRemoved_RemovesAllKeysInCollection() {
        var cacheKey = "remove-key";
        var uid      = Identifiers.NewUid();
        var indexKey = ReverseIndex.BuildKey(typeof(Student), new Student { Uid = uid });
        Assert.NotNull(indexKey);

        var mock = new Mock<ICacheProvider>();
        mock.Setup(x => x.CollectionMembersAsync(indexKey!, It.IsAny<CancellationToken>())).ReturnsAsync([cacheKey]);

        var advisor = new AdviceCommittedEvictCache<Student>(mock.Object, DefaultOptions());
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var repo    = Mock.Of<IRepository<Student>>();
        var changes = new CommitChanges<Student> {
            Removed = [new() { Uid = uid }],
        };

        var result = await advisor.AdviseAsync(ctx, repo, changes, CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
        mock.Verify(x => x.RemoveAsync(cacheKey, It.IsAny<CancellationToken>()), Times.Once);
        mock.Verify(x => x.CollectionClearAsync(indexKey!, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AdviseAsync_WhenEntitiesAdded_DoesNotEvict() {
        var mock = new Mock<ICacheProvider>();

        var advisor = new AdviceCommittedEvictCache<Student>(mock.Object, DefaultOptions());
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var repo    = Mock.Of<IRepository<Student>>();
        var changes = new CommitChanges<Student> {
            Added = [new() { Uid = Identifiers.NewUid() }],
        };

        var result = await advisor.AdviseAsync(ctx, repo, changes, CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
        mock.Verify(x => x.CollectionMembersAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        mock.Verify(x => x.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        mock.Verify(x => x.CollectionClearAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AdviseAsync_WhenSuppressed_LeavesCacheIntact() {
        var mock    = new Mock<ICacheProvider>();
        var advisor = new AdviceCommittedEvictCache<Student>(mock.Object, DefaultOptions());
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        ctx.Set(new QueryCacheEvictionSuppressed());
        var repo    = Mock.Of<IRepository<Student>>();
        var changes = new CommitChanges<Student> {
            Updated = [new() { Uid = Identifiers.NewUid() }],
            Removed = [new() { Uid = Identifiers.NewUid() }],
        };

        var result = await advisor.AdviseAsync(ctx, repo, changes, CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
        mock.Verify(x => x.CollectionMembersAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        mock.Verify(x => x.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        mock.Verify(x => x.CollectionClearAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AdviseAsync_WhenEvictionDisabled_LeavesCacheIntact() {
        var mock    = new Mock<ICacheProvider>();
        var options = Options.Create(new SchemataQueryCacheOptions { EvictionEnabled = false });
        var advisor = new AdviceCommittedEvictCache<Student>(mock.Object, options);
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var repo    = Mock.Of<IRepository<Student>>();
        var changes = new CommitChanges<Student> {
            Updated = [new() { Uid = Identifiers.NewUid() }],
            Removed = [new() { Uid = Identifiers.NewUid() }],
        };

        var result = await advisor.AdviseAsync(ctx, repo, changes, CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
        mock.Verify(x => x.CollectionMembersAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        mock.Verify(x => x.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        mock.Verify(x => x.CollectionClearAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AdviseAsync_WhenNoCollectionEntry_ReturnsContinueWithoutError() {
        var mock = new Mock<ICacheProvider>();
        mock.Setup(x => x.CollectionMembersAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var advisor = new AdviceCommittedEvictCache<Student>(mock.Object, DefaultOptions());
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var repo    = Mock.Of<IRepository<Student>>();
        var changes = new CommitChanges<Student> {
            Updated = [new() { Uid = Identifiers.NewUid() }],
        };

        var result = await advisor.AdviseAsync(ctx, repo, changes, CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
    }

    [Fact]
    public async Task AdviseAsync_DoesNotEvictEntriesForDifferentEntityId() {
        var otherUid   = Identifiers.NewUid();
        var other      = new Student { Uid = otherUid };
        var otherIndex = ReverseIndex.BuildKey(typeof(Student), other);
        Assert.NotNull(otherIndex);

        var targetUid   = Identifiers.NewUid();
        var targetIndex = ReverseIndex.BuildKey(typeof(Student), new Student { Uid = targetUid });
        Assert.NotNull(targetIndex);

        var mock = new Mock<ICacheProvider>();
        mock.Setup(x => x.CollectionMembersAsync(targetIndex!, It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var advisor = new AdviceCommittedEvictCache<Student>(mock.Object, DefaultOptions());
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var repo    = Mock.Of<IRepository<Student>>();
        var changes = new CommitChanges<Student> {
            Updated = [new() { Uid = targetUid }],
        };

        var result = await advisor.AdviseAsync(ctx, repo, changes, CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
        mock.Verify(x => x.CollectionMembersAsync(otherIndex!, It.IsAny<CancellationToken>()), Times.Never);
        mock.Verify(x => x.RemoveAsync(otherIndex!, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AdviseAsync_EndToEnd_EvictsResultStoredByResultCacheAdvisor() {
        var mock = new Mock<ICacheProvider>();
        mock.Setup(x => x.SetAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CacheEntryOptions>(),
                                   It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(x => x.CollectionAddAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CacheEntryOptions>(),
                                              It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var options = DefaultOptions();
        var write   = new AdviceResultCache<Student, Student, Student>(mock.Object, options);
        var evict   = new AdviceCommittedEvictCache<Student>(mock.Object, options);
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var repo    = Mock.Of<IRepository<Student>>();
        var student = new Student { Uid = Identifiers.NewUid(), FullName = "Zed" };
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

        var changes = new CommitChanges<Student> {
            Updated = [student],
        };

        await evict.AdviseAsync(ctx, repo, changes, CancellationToken.None);

        mock.Verify(x => x.RemoveAsync(cacheKey, It.IsAny<CancellationToken>()), Times.Once);
    }
}
