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

public class AdviceUpdateTimestampShould
{
    [Fact]
    public async Task Advise_TimestampEntity_UpdatesOnlyUpdateTime() {
        var advisor    = new AdviceUpdateTimestamp<Student>();
        var ctx        = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var repository = new Mock<IRepository<Student>>().Object;
        var original   = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var entity     = new Student { CreateTime = original, UpdateTime = original };
        var before     = DateTime.UtcNow;

        var result = await advisor.AdviseAsync(ctx, repository, entity, CancellationToken.None);

        var after = DateTime.UtcNow;
        Assert.Equal(AdviseResult.Continue, result);
        Assert.Equal(original, entity.CreateTime);
        Assert.NotNull(entity.UpdateTime);
        Assert.InRange(entity.UpdateTime!.Value, before, after);
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
        ctx.Set(new SuppressTimestamp());
        var repository = new Mock<IRepository<Student>>().Object;
        var original   = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var entity     = new Student { CreateTime = original, UpdateTime = original };

        var result = await advisor.AdviseAsync(ctx, repository, entity, CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
        Assert.Equal(original, entity.UpdateTime);
    }

    #region Nested type: PlainEntity

    public class PlainEntity
    {
        public long Id { get; set; }
    }

    #endregion
}
