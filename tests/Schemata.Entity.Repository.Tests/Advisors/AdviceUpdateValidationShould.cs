using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Entity.Repository.Advisors;
using Schemata.Entity.Repository.Tests.Fixtures;
using Xunit;

namespace Schemata.Entity.Repository.Tests.Advisors;

public class AdviceUpdateValidationShould
{
    [Fact]
    public async Task Advise_NoValidators_Continues() {
        var advisor    = new AdviceUpdateValidation<Student>();
        var ctx        = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var repository = new Mock<IRepository<Student>>().Object;
        var entity     = new Student();

        var result = await advisor.AdviseAsync(ctx, repository, entity, CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
    }

    [Fact]
    public async Task Advise_Suppressed_Continues() {
        var advisor = new AdviceUpdateValidation<Student>();
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        ctx.Set(new SuppressUpdateValidation());
        var repository = new Mock<IRepository<Student>>().Object;
        var entity     = new Student();

        var result = await advisor.AdviseAsync(ctx, repository, entity, CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
    }
}
