using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Schemata.Abstractions.Exceptions;
using Schemata.Resource.Foundation.Advisors;
using Schemata.Resource.Foundation.Tests.Fixtures;
using Xunit;

namespace Schemata.Resource.Foundation.Tests.ResourceOperationHandler;

public class OperationHandlerDeleteShould
{
    private readonly HandlerFixture _fixture = new();

    [Fact]
    public async Task Delete_ExistingEntity_RemovesAndCommits() {
        var handler = _fixture.CreateHandler();
        var entity  = _fixture.Students[0];
        var before  = _fixture.Students.Count;

        var deleted = await handler.DeleteAsync(entity.CanonicalName!, null, false, null, null);

        Assert.True(deleted);
        Assert.Equal(before - 1, _fixture.Students.Count);
        _fixture.Repository.Verify(r => r.CommitAsync(CancellationToken.None), Times.AtLeastOnce);
    }

    [Fact]
    public async Task Delete_ETagMismatch_ThrowsConcurrencyException() {
        var handler = _fixture.CreateHandler(services => {
            services.TryAddScoped<IResourceDeleteAdvisor<Student>, AdviceDeleteFreshness<Student>>();
        });
        var entity = _fixture.Students[0]; // already has Timestamp set

        await Assert.ThrowsAsync<ConcurrencyException>(() => handler.DeleteAsync(entity.CanonicalName!, "W/\"wrongtag\"", false, null, null));
    }

    [Fact]
    public async Task Delete_Force_BypassesFreshnessCheck() {
        var handler = _fixture.CreateHandler(services => {
            services.TryAddScoped<IResourceDeleteAdvisor<Student>, AdviceDeleteFreshness<Student>>();
        });
        var entity = _fixture.Students[0]; // has Timestamp set

        var deleted = await handler.DeleteAsync(entity.CanonicalName!, "W/\"wrongtag\"", true, null, null);

        Assert.True(deleted);
    }
}
