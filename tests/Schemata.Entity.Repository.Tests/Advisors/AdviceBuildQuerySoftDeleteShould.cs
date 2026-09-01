using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Entity.Repository.Advisors;
using Schemata.Entity.Repository.Tests.Fixtures;
using Xunit;

namespace Schemata.Entity.Repository.Tests.Advisors;

public class AdviceBuildQuerySoftDeleteShould
{
    [Fact]
    public async Task Advise_SoftDeleteType_FiltersDeletedEntities() {
        var advisor    = new AdviceBuildQuerySoftDelete<Student>();
        var ctx        = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var repository = new Mock<IRepository<Student>>().Object;
        var data = new List<Student> {
            new() { Uid = Guid.NewGuid(), FullName = "Active", DeleteTime  = null },
            new() { Uid = Guid.NewGuid(), FullName = "Deleted", DeleteTime = DateTime.UtcNow },
        }.AsQueryable();
        var container = new QueryContainer<Student>(repository, data);

        var result = await advisor.AdviseAsync(ctx, container, CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
        var filtered = container.Query.ToList();
        Assert.Single(filtered);
        Assert.Equal("Active", filtered[0].FullName);
    }

    [Fact]
    public async Task Advise_NonSoftDeleteType_DoesNotFilter() {
        var advisor    = new AdviceBuildQuerySoftDelete<PlainEntity>();
        var ctx        = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var repository = new Mock<IRepository<PlainEntity>>().Object;
        var data       = new List<PlainEntity> { new() { Id = 1 }, new() { Id = 2 } }.AsQueryable();
        var container  = new QueryContainer<PlainEntity>(repository, data);

        var result = await advisor.AdviseAsync(ctx, container, CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
        Assert.Equal(2, container.Query.Count());
    }

    [Fact]
    public async Task Advise_Suppressed_DoesNotFilter() {
        var advisor = new AdviceBuildQuerySoftDelete<Student>();
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        ctx.Set(new QuerySoftDeleteSuppressed());
        var repository = new Mock<IRepository<Student>>().Object;
        var data = new List<Student> {
            new() { Uid = Guid.NewGuid(), DeleteTime = null },
            new() { Uid = Guid.NewGuid(), DeleteTime = DateTime.UtcNow },
        }.AsQueryable();
        var container = new QueryContainer<Student>(repository, data);

        var result = await advisor.AdviseAsync(ctx, container, CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
        Assert.Equal(2, container.Query.Count());
    }
}
