using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Entity.Repository.Advisors;
using Schemata.Entity.Repository.Tests.Fixtures;
using Xunit;

namespace Schemata.Entity.Repository.Tests.Advisors;

public class AdviceAddCanonicalNameShould
{
    [Fact]
    public async Task Advise_EntityWithCanonicalNameAndPattern_SetsCanonicalName() {
        var advisor    = new AdviceAddCanonicalName<Student>();
        var ctx        = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var repository = new Mock<IRepository<Student>>().Object;
        var entity     = new Student { Uid = Guid.NewGuid(), Name = "alice" };

        var result = await advisor.AdviseAsync(ctx, repository, entity, CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
        Assert.Equal("students/alice", entity.CanonicalName);
    }

    [Fact]
    public async Task Advise_EntityWithoutCanonicalNameInterface_Continues() {
        var advisor    = new AdviceAddCanonicalName<PlainEntity>();
        var ctx        = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var repository = new Mock<IRepository<PlainEntity>>().Object;
        var entity     = new PlainEntity();

        var result = await advisor.AdviseAsync(ctx, repository, entity, CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
    }

    [Fact]
    public async Task Advise_EntityWithNoPattern_DoesNotSetCanonicalName() {
        var advisor    = new AdviceAddCanonicalName<NoPatternEntity>();
        var ctx        = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var repository = new Mock<IRepository<NoPatternEntity>>().Object;
        var entity     = new NoPatternEntity { Name = "test", CanonicalName = null };

        var result = await advisor.AdviseAsync(ctx, repository, entity, CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
        Assert.Null(entity.CanonicalName);
    }

    #region Test entities

    public class PlainEntity
    {
        public long Id { get; set; }
    }

    public class NoPatternEntity : ICanonicalName
    {
        #region ICanonicalName Members

        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }

        #endregion
    }

    #endregion
}
