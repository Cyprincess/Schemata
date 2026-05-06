using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Resource.Foundation;
using Schemata.Resource.Foundation.Advisors;
using Schemata.Resource.Tests.Fixtures;
using Xunit;

namespace Schemata.Resource.Tests.Advisors;

public class AdviceValidationShould
{
    [Fact]
    public async Task Create_SuppressValidation_ReturnsContinue() {
        var advisor = new AdviceCreateRequestValidation<Student, Student>();
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        ctx.Set(new CreateRequestValidationSuppressed());
        var request   = new Student { FullName = "Suppressed" };
        var container = new ResourceRequestContainer<Student>();

        var result = await advisor.AdviseAsync(ctx, request, container, null);

        Assert.Equal(AdviseResult.Continue, result);
    }

    [Fact]
    public async Task Create_SuppressValidationAndValidateOnly_ThrowsNoContentException() {
        var advisor = new AdviceCreateRequestValidation<Student, Student>();
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        ctx.Set(new CreateRequestValidationSuppressed());
        var request   = new Student { FullName = "DryRun", ValidateOnly = true };
        var container = new ResourceRequestContainer<Student>();

        await Assert.ThrowsAsync<NoContentException>(() => advisor.AdviseAsync(ctx, request, container, null));
    }
}
