using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.Repository.Advisors;
using Schemata.Entity.Repository.Tests.Fixtures;
using Xunit;

namespace Schemata.Entity.Repository.Tests.Advisors;

public class AdviceUpdateConcurrencyShould
{
    [Fact]
    public async Task Advise_MatchingTimestamp_SetsNewTimestamp() {
        var shared  = Guid.NewGuid();
        var advisor = new AdviceUpdateConcurrency<Student>();
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var mock    = new Mock<IRepository<Student>>();
        var stored  = new Student { Id = 1, Timestamp = shared };
        mock.Setup(r => r.GetAsync<IConcurrency>(It.IsAny<Student>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);
        var entity = new Student { Id = 1, Timestamp = shared };

        var result = await advisor.AdviseAsync(ctx, mock.Object, entity, CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
        Assert.NotNull(entity.Timestamp);
        Assert.NotEqual(shared, entity.Timestamp);
    }

    [Fact]
    public async Task Advise_MismatchedTimestamp_ThrowsConcurrencyException() {
        var advisor = new AdviceUpdateConcurrency<Student>();
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var mock    = new Mock<IRepository<Student>>();
        var stored  = new Student { Id = 1, Timestamp = Guid.NewGuid() };
        mock.Setup(r => r.GetAsync<IConcurrency>(It.IsAny<Student>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);
        var entity = new Student { Id = 1, Timestamp = Guid.NewGuid() };

        await Assert.ThrowsAsync<ConcurrencyException>(() => advisor.AdviseAsync(
                                                           ctx,
                                                           mock.Object,
                                                           entity,
                                                           CancellationToken.None
                                                       )
        );
    }

    [Fact]
    public async Task Advise_StoredEntityNull_Continues() {
        var advisor = new AdviceUpdateConcurrency<Student>();
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var mock    = new Mock<IRepository<Student>>();
        mock.Setup(r => r.GetAsync<IConcurrency>(It.IsAny<Student>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IConcurrency?)null);
        var entity = new Student { Id = 1, Timestamp = Guid.NewGuid() };

        var result = await advisor.AdviseAsync(ctx, mock.Object, entity, CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
    }

    [Fact]
    public async Task Advise_NonConcurrencyEntity_Continues() {
        var advisor    = new AdviceUpdateConcurrency<PlainEntity>();
        var ctx        = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var repository = new Mock<IRepository<PlainEntity>>().Object;
        var entity     = new PlainEntity();

        var result = await advisor.AdviseAsync(ctx, repository, entity, CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
    }

    [Fact]
    public async Task Advise_Suppressed_DoesNotCheck() {
        var advisor = new AdviceUpdateConcurrency<Student>();
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        ctx.Set(new ConcurrencySuppressed());
        var repository = new Mock<IRepository<Student>>().Object;
        var entity     = new Student { Id = 1, Timestamp = Guid.NewGuid() };
        var original   = entity.Timestamp;

        var result = await advisor.AdviseAsync(ctx, repository, entity, CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
        Assert.Equal(original, entity.Timestamp);
    }

    #region Nested type: PlainEntity

    public class PlainEntity
    {
        public long Id { get; set; }
    }

    #endregion
}
