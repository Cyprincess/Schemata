using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Resource.Foundation.Advisors;
using Schemata.Resource.Foundation.Tests.Fixtures;
using Xunit;

namespace Schemata.Resource.Foundation.Tests.Advisors;

public class AdviceValidationShould
{
    [Fact]
    public async Task Create_SuppressValidation_ReturnsContinue() {
        var advisor = new AdviceCreateRequestValidation<Student, Student>();
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        ctx.Set(new SuppressCreateRequestValidation());
        var request = new Student { FullName = "Suppressed" };

        var result = await advisor.AdviseAsync(ctx, request, null);

        Assert.Equal(AdviseResult.Continue, result);
    }

    [Fact]
    public async Task Create_SuppressValidationAndValidateOnly_ThrowsNoContentException() {
        var advisor = new AdviceCreateRequestValidation<Student, Student>();
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        ctx.Set(new SuppressCreateRequestValidation());
        var request = new Student { FullName = "DryRun", ValidateOnly = true };

        await Assert.ThrowsAsync<NoContentException>(() => advisor.AdviseAsync(ctx, request, null));
    }

    [Fact]
    public async Task Create_NoValidators_ReturnsContinue() {
        var advisor = new AdviceCreateRequestValidation<Student, Student>();
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var request = new Student { FullName = "Valid" };

        var result = await advisor.AdviseAsync(ctx, request, null);

        Assert.Equal(AdviseResult.Continue, result);
    }

    [Fact]
    public async Task Create_NoValidatorsValidateOnly_ThrowsNoContentException() {
        var advisor = new AdviceCreateRequestValidation<Student, Student>();
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var request = new Student { FullName = "DryRunNoValidators", ValidateOnly = true };

        await Assert.ThrowsAsync<NoContentException>(() => advisor.AdviseAsync(ctx, request, null));
    }
}
