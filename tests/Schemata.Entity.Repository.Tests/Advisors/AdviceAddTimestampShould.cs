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

public class AdviceAddTimestampShould
{
    [Fact]
    public async Task Advise_TimestampEntity_SetsBothTimes() {
        var now        = new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);
        var advisor    = new AdviceAddTimestamp<Student>(new FakeTimeProvider(now));
        var ctx        = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var repository = new Mock<IRepository<Student>>().Object;
        var entity     = new Student { CreateTime = null, UpdateTime = null };

        var result = await advisor.AdviseAsync(ctx, repository, entity, CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
        Assert.Equal(now.UtcDateTime, entity.CreateTime);
        Assert.Equal(now.UtcDateTime, entity.UpdateTime);
    }

    [Fact]
    public async Task Advise_Create_CreateTimeEqualsUpdateTime() {
        var advisor    = new AdviceAddTimestamp<Student>();
        var ctx        = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var repository = new Mock<IRepository<Student>>().Object;
        var entity     = new Student { CreateTime = null, UpdateTime = null };

        await advisor.AdviseAsync(ctx, repository, entity, CancellationToken.None);

        Assert.Equal(entity.CreateTime, entity.UpdateTime);
    }

    [Fact]
    public async Task Advise_NonTimestampEntity_Continues() {
        var advisor    = new AdviceAddTimestamp<PlainEntity>();
        var ctx        = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var repository = new Mock<IRepository<PlainEntity>>().Object;
        var entity     = new PlainEntity();

        var result = await advisor.AdviseAsync(ctx, repository, entity, CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
    }

    [Fact]
    public async Task Advise_Suppressed_DoesNotSetTimes() {
        var advisor = new AdviceAddTimestamp<Student>();
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        ctx.Set(new TimestampSuppressed());
        var repository = new Mock<IRepository<Student>>().Object;
        var entity     = new Student { CreateTime = null, UpdateTime = null };

        var result = await advisor.AdviseAsync(ctx, repository, entity, CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
        Assert.Null(entity.CreateTime);
        Assert.Null(entity.UpdateTime);
    }
}
