using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Resource.Foundation.Advisors;
using Schemata.Resource.Foundation.Commands;
using Schemata.Resource.Tests.Fixtures;
using Xunit;

namespace Schemata.Resource.Tests.Advisors;

public class ResourceValidationPipelineAdvisorShould
{
    [Fact]
    public async Task Create_SuppressValidation_Continues() {
        var advisor = new ResourceCreateValidationPipelineAdvisor<Student, Student, Student>();
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        ctx.Set(new CreateRequestValidationSuppressed());
        var request  = new Student { FullName = "Suppressed" };
        var envelope = new CreateResourceRequest<Student, Student, Student>(request, null);

        var continued = false;
        var result = await advisor.AdviseAsync(ctx, envelope, _ => {
            continued = true;
            return Task.FromResult(new CreateResultBase<Student>());
        }, CancellationToken.None);

        Assert.True(continued);
    }

    [Fact]
    public async Task Create_SuppressValidationAndValidateOnly_ThrowsNoContentException() {
        var advisor = new ResourceCreateValidationPipelineAdvisor<Student, Student, Student>();
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        ctx.Set(new CreateRequestValidationSuppressed());
        var request  = new Student { FullName = "DryRun", ValidateOnly = true };
        var envelope = new CreateResourceRequest<Student, Student, Student>(request, null);

        await Assert.ThrowsAsync<NoContentException>(
            () => advisor.AdviseAsync(ctx, envelope, _ => Task.FromResult(new CreateResultBase<Student>()), CancellationToken.None));
    }

    [Fact]
    public async Task Update_SuppressValidation_Continues() {
        var advisor = new ResourceUpdateValidationPipelineAdvisor<Student, Student, Student>();
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        ctx.Set(new UpdateRequestValidationSuppressed());
        var request  = new Student { FullName = "Suppressed" };
        var envelope = new UpdateResourceRequest<Student, Student, Student>("students/1", request, null);

        var continued = false;
        await advisor.AdviseAsync(ctx, envelope, _ => {
            continued = true;
            return Task.FromResult(new UpdateResultBase<Student>());
        }, CancellationToken.None);

        Assert.True(continued);
    }

    [Fact]
    public async Task Update_SuppressValidationAndValidateOnly_ThrowsNoContentException() {
        var advisor = new ResourceUpdateValidationPipelineAdvisor<Student, Student, Student>();
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        ctx.Set(new UpdateRequestValidationSuppressed());
        var request  = new Student { FullName = "DryRun", ValidateOnly = true };
        var envelope = new UpdateResourceRequest<Student, Student, Student>("students/1", request, null);

        await Assert.ThrowsAsync<NoContentException>(
            () => advisor.AdviseAsync(ctx, envelope, _ => Task.FromResult(new UpdateResultBase<Student>()), CancellationToken.None));
    }
}
