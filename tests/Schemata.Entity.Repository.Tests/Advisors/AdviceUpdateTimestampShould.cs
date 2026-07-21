using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Entity.Repository.Advisors;
using Schemata.Entity.Repository.Tests.Fixtures;
using Xunit;

namespace Schemata.Entity.Repository.Tests.Advisors;

public class AdviceUpdateTimestampShould
{
    [Fact]
    public async Task Advise_TimestampEntity_UpdatesOnlyUpdateTime() {
        var now        = new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);
        var advisor    = new AdviceUpdateTimestamp<Student>(new FakeTimeProvider(now));
        var ctx        = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var repository = new Mock<IRepository<Student>>().Object;
        var original   = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var entity     = new Student { CreateTime = original, UpdateTime = original };

        var result = await advisor.AdviseAsync(ctx, repository, entity, CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
        Assert.Equal(original, entity.CreateTime);
        Assert.Equal(now.UtcDateTime, entity.UpdateTime);
    }

    [Fact]
    public async Task Advise_NonTimestampEntity_Continues() {
        var advisor    = new AdviceUpdateTimestamp<PlainEntity>();
        var ctx        = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var repository = new Mock<IRepository<PlainEntity>>().Object;
        var entity     = new PlainEntity();

        var result = await advisor.AdviseAsync(ctx, repository, entity, CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
    }

    [Fact]
    public async Task Advise_Suppressed_DoesNotUpdateTime() {
        var advisor = new AdviceUpdateTimestamp<Student>();
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        ctx.Set(new TimestampSuppressed());
        var repository = new Mock<IRepository<Student>>().Object;
        var original   = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var entity     = new Student { CreateTime = original, UpdateTime = original };

        var result = await advisor.AdviseAsync(ctx, repository, entity, CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
        Assert.Equal(original, entity.UpdateTime);
    }
}
