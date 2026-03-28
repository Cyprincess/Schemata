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

public class AdviceAddSoftDeleteShould
{
    [Fact]
    public async Task Advise_SoftDeleteEntity_ClearsDeleteTime() {
        var advisor    = new AdviceAddSoftDelete<Student>();
        var ctx        = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var repository = new Mock<IRepository<Student>>().Object;
        var entity     = new Student { DeleteTime = DateTime.UtcNow };

        var result = await advisor.AdviseAsync(ctx, repository, entity, CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
        Assert.Null(entity.DeleteTime);
    }

    [Fact]
    public async Task Advise_NonSoftDeleteEntity_Continues() {
        var advisor    = new AdviceAddSoftDelete<PlainEntity>();
        var ctx        = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var repository = new Mock<IRepository<PlainEntity>>().Object;
        var entity     = new PlainEntity();

        var result = await advisor.AdviseAsync(ctx, repository, entity, CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
    }

    [Fact]
    public async Task Advise_Suppressed_DoesNotClearDeleteTime() {
        var advisor = new AdviceAddSoftDelete<Student>();
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        ctx.Set(new SoftDeleteSuppressed());
        var repository = new Mock<IRepository<Student>>().Object;
        var existing   = DateTime.UtcNow;
        var entity     = new Student { DeleteTime = existing };

        var result = await advisor.AdviseAsync(ctx, repository, entity, CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
        Assert.Equal(existing, entity.DeleteTime);
    }

    #region Nested type: PlainEntity

    public class PlainEntity
    {
        public long Id { get; set; }
    }

    #endregion
}
