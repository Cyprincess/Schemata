using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Exceptions;
using Schemata.Resource.Foundation.Advisors;
using Schemata.Resource.Foundation.Tests.Fixtures;
using Xunit;

namespace Schemata.Resource.Foundation.Tests.ResourceOperationHandler;

public class OperationHandlerUpdateShould
{
    private readonly HandlerFixture _fixture = new();

    [Fact]
    public async Task Update_ValidRequest_UpdatesEntityAndCommits() {
        var handler = _fixture.CreateHandler();
        var entity  = _fixture.Students[0];
        var request = new Student { FullName = "Alice Updated", Age = entity.Age, Grade = entity.Grade };

        var result = await handler.UpdateAsync(request, entity, null, null);

        Assert.True(result.IsAllowed());
        _fixture.Repository.Verify(r => r.UpdateAsync(It.IsAny<Student>(), default), Times.Once);
        _fixture.Repository.Verify(r => r.CommitAsync(default), Times.AtLeastOnce);
    }

    [Fact]
    public async Task Update_WithUpdateMask_OnlyAppliesMaskedFields() {
        var handler  = _fixture.CreateHandler();
        var entity   = _fixture.Students[0];
        var original = entity.Age;
        var request = new Student {
            FullName = "Alice Renamed", Age = 999, UpdateMask = "FullName",
        };

        await handler.UpdateAsync(request, entity, null, null);

        Assert.Equal("Alice Renamed", entity.FullName);
        Assert.Equal(original, entity.Age);
    }

    [Fact]
    public async Task Update_ETagMismatch_ThrowsConcurrencyException() {
        var handler = _fixture.CreateHandler(services => {
            services.AddSingleton<IResourceUpdateAdvisor<Student, Student>, AdviceUpdateFreshness<Student, Student>>();
        });
        var entity  = _fixture.Students[0]; // already has Timestamp set
        var request = new Student { EntityTag = "W/\"wrongtag\"" };

        await Assert.ThrowsAsync<ConcurrencyException>(() => handler.UpdateAsync(request, entity, null, null));
    }
}
