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

public class AdviceRemoveSoftDeleteShould
{
    [Fact]
    public async Task Advise_SoftDeleteEntity_SetsDeleteTimeAndReturnsHandle() {
        var now     = new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);
        var advisor = new AdviceRemoveSoftDelete<Student>(new FakeTimeProvider(now));
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var mock    = new Mock<IRepository<Student>>();
        mock.Setup(r => r.UpdateAsync(It.IsAny<Student>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var entity = new Student { DeleteTime = null };

        var result = await advisor.AdviseAsync(ctx, mock.Object, entity, CancellationToken.None);

        Assert.Equal(AdviseResult.Handle, result);
        Assert.Equal(now.UtcDateTime, entity.DeleteTime);
        mock.Verify(r => r.UpdateAsync(entity, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Advise_NonSoftDeleteEntity_Continues() {
        var advisor    = new AdviceRemoveSoftDelete<PlainEntity>();
        var ctx        = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var repository = new Mock<IRepository<PlainEntity>>().Object;
        var entity     = new PlainEntity();

        var result = await advisor.AdviseAsync(ctx, repository, entity, CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
    }

    [Fact]
    public async Task Advise_Suppressed_ContinuesWithoutSoftDelete() {
        var advisor = new AdviceRemoveSoftDelete<Student>();
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        ctx.Set(new SoftDeleteSuppressed());
        var repository = new Mock<IRepository<Student>>().Object;
        var entity     = new Student { DeleteTime = null };

        var result = await advisor.AdviseAsync(ctx, repository, entity, CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
        Assert.Null(entity.DeleteTime);
    }
}
