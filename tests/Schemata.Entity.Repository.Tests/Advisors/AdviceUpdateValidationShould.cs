using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Errors;
using Schemata.Entity.Repository.Advisors;
using Schemata.Entity.Repository.Tests.Fixtures;
using Schemata.Validation.Skeleton.Advisors;
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

        var result = await advisor.AdviseAsync(ctx, repository, entity, default, CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
    }

    [Fact]
    public async Task Advise_IgnoreValidation_SkipsValidators() {
        var validator = new Mock<IValidationAdvisor<Student>>();
        validator.SetupGet(value => value.Order).Returns(0);
        var services = new ServiceCollection();
        services.AddSingleton(validator.Object);

        var advisor    = new AdviceUpdateValidation<Student>();
        var ctx        = new AdviceContext(services.BuildServiceProvider());
        var repository = new Mock<IRepository<Student>>().Object;
        var entity     = new Student();

        var result = await advisor.AdviseAsync(
            ctx,
            repository,
            entity,
            new MutationOptions(IgnoreValidation: true), CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
        validator.Verify(value => value.AdviseAsync(
                              It.IsAny<AdviceContext>(),
                              It.IsAny<Operations>(),
                              It.IsAny<Student>(),
                              It.IsAny<IList<ErrorFieldViolation>>(),
                              It.IsAny<CancellationToken>()),
                          Times.Never);
    }

    [Fact]
    public async Task Advise_HandledValidation_Continues() {
        var validator = new Mock<IValidationAdvisor<Student>>();
        validator.SetupGet(value => value.Order).Returns(0);
        validator.Setup(value => value.AdviseAsync(It.IsAny<AdviceContext>(), Operations.Update, It.IsAny<Student>(), It.IsAny<IList<ErrorFieldViolation>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AdviseResult.Handle);
        var services = new ServiceCollection();
        services.AddSingleton(validator.Object);

        var advisor    = new AdviceUpdateValidation<Student>();
        var ctx        = new AdviceContext(services.BuildServiceProvider());
        var repository = new Mock<IRepository<Student>>().Object;
        var entity     = new Student();

        var result = await advisor.AdviseAsync(ctx, repository, entity, default, CancellationToken.None);

        Assert.Equal(AdviseResult.Continue, result);
    }

    [Fact]
    public async Task Advise_BlockedValidation_Throws() {
        var validator = new Mock<IValidationAdvisor<Student>>();
        validator.SetupGet(value => value.Order).Returns(0);
        validator.Setup(value => value.AdviseAsync(It.IsAny<AdviceContext>(), Operations.Update, It.IsAny<Student>(), It.IsAny<IList<ErrorFieldViolation>>(), It.IsAny<CancellationToken>()))
            .Callback<AdviceContext, Operations, Student, IList<ErrorFieldViolation>, CancellationToken>((_, _, _, errors, _) => errors.Add(new() { Field = "Name", Reason = "REQUIRED" }))
            .ReturnsAsync(AdviseResult.Block);
        var services = new ServiceCollection();
        services.AddSingleton(validator.Object);

        var advisor    = new AdviceUpdateValidation<Student>();
        var ctx        = new AdviceContext(services.BuildServiceProvider());
        var repository = new Mock<IRepository<Student>>().Object;
        var entity     = new Student();

        var error = await Assert.ThrowsAsync<Schemata.Abstractions.Exceptions.ValidationException>(
            () => advisor.AdviseAsync(ctx, repository, entity, default, CancellationToken.None));
        Assert.Single(error.Details!.OfType<Schemata.Abstractions.Errors.BadRequestDetail>().SelectMany(detail => detail.FieldViolations!), violation => violation.Field == "Name");
    }
}
