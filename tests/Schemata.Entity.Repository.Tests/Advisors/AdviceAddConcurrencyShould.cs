using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Entity.Repository.Advisors;
using Schemata.Entity.Repository.Tests.Fixtures;
using Xunit;

namespace Schemata.Entity.Repository.Tests.Advisors;

public class AdviceAddConcurrencyShould
{
    [Fact]
    public async Task Advise_ConcurrencyEntity_SetsTimestamp() {
        var advisor    = new AdviceAddConcurrency<Student>();
        var ctx        = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var repository = new Mock<IRepository<Student>>().Object;
        var entity     = new Student { Timestamp = null };

        var result = await advisor.AdviseAsync(ctx, repository, entity, CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
        Assert.NotNull(entity.Timestamp);
        Assert.NotEqual(Guid.Empty, entity.Timestamp!.Value);
    }

    [Fact]
    public async Task Advise_NonConcurrencyEntity_Continues() {
        var advisor    = new AdviceAddConcurrency<PlainEntity>();
        var ctx        = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var repository = new Mock<IRepository<PlainEntity>>().Object;
        var entity     = new PlainEntity();

        var result = await advisor.AdviseAsync(ctx, repository, entity, CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
    }

    [Fact]
    public async Task Advise_Suppressed_DoesNotSetTimestamp() {
        var advisor = new AdviceAddConcurrency<Student>();
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        ctx.Set(new ConcurrencySuppressed());
        var repository = new Mock<IRepository<Student>>().Object;
        var entity     = new Student { Timestamp = null };

        var result = await advisor.AdviseAsync(ctx, repository, entity, CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
        Assert.Null(entity.Timestamp);
    }
}
