using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Exceptions;
using Schemata.Resource.Foundation.Advisors;
using Schemata.Resource.Foundation.Tests.Fixtures;
using Xunit;

namespace Schemata.Resource.Foundation.Tests.ResourceOperationHandler;

public class OperationHandlerCreateShould
{
    private readonly HandlerFixture _fixture = new();

    [Fact]
    public async Task Create_ValidRequest_AddsEntityAndCommits() {
        var handler = _fixture.CreateHandler();
        var request = new Student { FullName = "Charlie", Age = 20, Grade = 3 };
        var before  = _fixture.Students.Count;

        var result = await handler.CreateAsync(request, null, null);

        Assert.True(result.IsAllowed());
        Assert.Equal(before + 1, _fixture.Students.Count);
    }

    [Fact]
    public async Task Create_ValidRequest_Commits() {
        var handler = _fixture.CreateHandler();
        var request = new Student { FullName = "Charlie", Age = 20, Grade = 3 };

        await handler.CreateAsync(request, null, null);

        _fixture.Repository.Verify(r => r.CommitAsync(default), Times.AtLeastOnce);
    }

    [Fact]
    public async Task Create_ValidateOnly_ThrowsNoContent() {
        var handler = _fixture.CreateHandler(services => {
            services.AddSingleton<IResourceCreateRequestAdvisor<Student, Student>,
                AdviceCreateRequestValidation<Student, Student>>();
        });
        var request = new Student {
            FullName     = "DryRun",
            Age          = 20,
            Grade        = 1,
            ValidateOnly = true,
        };

        await Assert.ThrowsAsync<NoContentException>(() => handler.CreateAsync(request, null, null));
    }
}
