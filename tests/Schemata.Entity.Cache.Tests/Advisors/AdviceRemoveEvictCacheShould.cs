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

public class AdviceRemoveEvictCacheShould
{
    private static IOptions<SchemataQueryCacheOptions> DefaultOptions() {
        return Options.Create(new SchemataQueryCacheOptions());
    }

    [Fact]
    public void Order_EqualsOrdersMax() {
        var mock    = new Mock<ICacheProvider>();
        var advisor = new AdviceRemoveEvictCache<Student>(mock.Object, DefaultOptions());
        Assert.Equal(SchemataConstants.Orders.Max, advisor.Order);
    }

    [Fact]
    public async Task AdviseAsync_WhenEntityRemoved_RemovesAllKeysInCollection() {
        var cacheKey = "remove-key";
        var indexKey = ReverseIndex.BuildKey(typeof(Student), new Student { Id = 9 });
        Assert.NotNull(indexKey);

        var mock = new Mock<ICacheProvider>();
        mock.Setup(x => x.CollectionMembersAsync(indexKey!, It.IsAny<CancellationToken>())).ReturnsAsync([cacheKey]);

        var advisor = new AdviceRemoveEvictCache<Student>(mock.Object, DefaultOptions());
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var repo    = new Mock<IRepository<Student>>().Object;
        var entity  = new Student { Id = 9 };

        var result = await advisor.AdviseAsync(ctx, repo, entity, CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
        mock.Verify(x => x.RemoveAsync(cacheKey, It.IsAny<CancellationToken>()), Times.Once);
        mock.Verify(x => x.CollectionClearAsync(indexKey!, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AdviseAsync_WhenSuppressed_LeavesCacheIntact() {
        var mock    = new Mock<ICacheProvider>();
        var advisor = new AdviceRemoveEvictCache<Student>(mock.Object, DefaultOptions());
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
        var advisor = new AdviceRemoveEvictCache<Student>(mock.Object, options);
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var repo    = new Mock<IRepository<Student>>().Object;
        var entity  = new Student { Id = 11 };

        var result = await advisor.AdviseAsync(ctx, repo, entity, CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
        mock.Verify(x => x.CollectionMembersAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        mock.Verify(x => x.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        mock.Verify(x => x.CollectionClearAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
