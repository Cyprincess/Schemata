using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Entity.Cache.Advisors;
using Schemata.Entity.Cache.Tests.Fixtures;
using Schemata.Entity.Repository;
using Xunit;

namespace Schemata.Entity.Cache.Tests.Advisors;

[Trait("Layer", "Unit")]
public class AdviceResultCacheShould
{
    [Fact]
    public async Task Fill_WithoutQuerySnapshot_DoesNotPublishResult() {
        using var cache = new QueryCacheTestContext();
        await cache.Fill(cache.Query<int>(QueryOperation.Count), 7);
        Assert.Equal(AdviseResult.Continue, await cache.Read(cache.Query<int>(QueryOperation.Count)));
    }

    [Fact]
    public async Task Fill_WithNullResult_LeavesQueryUncached() {
        using var cache = new QueryCacheTestContext();
        var query = cache.Query<Student>(QueryOperation.FirstOrDefault);
        await cache.Read(query);
        await cache.Fill(query, null!);
        Assert.Equal(AdviseResult.Continue, await cache.Read(cache.Query<Student>(QueryOperation.FirstOrDefault)));
    }

    [Fact]
    public async Task Fill_UsesConfiguredAbsoluteLifetime_DespiteRepeatedReads() {
        using var cache = new QueryCacheTestContext();
        cache.Options.Ttl = TimeSpan.FromSeconds(7);
        var query = cache.Query<int>(QueryOperation.Count);
        await cache.Read(query);
        await cache.Fill(query, 7);

        cache.Elapsed = TimeSpan.FromSeconds(6);
        var hit = cache.Query<int>(QueryOperation.Count);
        Assert.Equal(AdviseResult.Handle, await cache.Read(hit));
        Assert.Equal(7, hit.Result);
        cache.Elapsed = TimeSpan.FromSeconds(7);
        Assert.Equal(AdviseResult.Continue, await cache.Read(cache.Query<int>(QueryOperation.Count)));
    }

    [Fact]
    public async Task Fill_WhenSuppressed_DoesNotReadOrReplaceCachedResult() {
        using var cache = new QueryCacheTestContext();
        var query = cache.Query<int>(QueryOperation.Count);
        await cache.Read(query);
        await cache.Fill(query, 7);

        using (cache.Advice.Use<QueryCacheSuppressed>()) {
            Assert.Equal(AdviseResult.Continue, await cache.Read(cache.Query<int>(QueryOperation.Count)));
            await cache.Fill(query, 9);
        }

        var next = cache.Query<int>(QueryOperation.Count);
        Assert.Equal(AdviseResult.Handle, await cache.Read(next));
        Assert.Equal(7, next.Result);
    }

    [Fact]
    public async Task Fill_AfterQuerySuppressionEnds_StillRequiresSnapshot() {
        using var cache = new QueryCacheTestContext();
        var query = cache.Query<int>(QueryOperation.Count);
        using (cache.Advice.Use<QueryCacheSuppressed>()) {
            await cache.Read(query);
        }
        await cache.Fill(query, 7);
        Assert.Equal(AdviseResult.Continue, await cache.Read(cache.Query<int>(QueryOperation.Count)));
    }

    [Fact]
    public async Task Join_Between_Read_And_Fill_Preserves_PreTransaction_Result() {
        using var cache = new QueryCacheTestContext();
        var original = cache.Query<Student>(QueryOperation.FirstOrDefault);
        await cache.Read(original);
        await cache.Fill(original, new Student { FullName = "Original" });

        using var services = new ServiceCollection().BuildServiceProvider();
        var repository = new Mock<RepositoryBase<Student>>(services) { CallBase = true };
        repository.As<IQueryCacheKeyProvider>()
            .Setup(provider => provider.GetQueryCacheKey(It.IsAny<IQueryable<Student>>()))
            .Returns("students");
        var query = cache.Query<Student>(QueryOperation.FirstOrDefault, repository.Object);
        Assert.Equal(AdviseResult.Handle, await cache.Read(query));
        var uow = new Mock<IUnitOfWork>();
        repository.Object.Join(uow.Object);

        await cache.Fill(query, new Student { FullName = "Transaction" });
        var joined = cache.Query<Student>(QueryOperation.FirstOrDefault, repository.Object);
        Assert.Equal(AdviseResult.Continue, await cache.Read(joined));

        var independent = cache.Query<Student>(QueryOperation.FirstOrDefault);
        Assert.Equal(AdviseResult.Handle, await cache.Read(independent));
        Assert.Equal("Original", independent.Result!.FullName);
    }

}
