using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Schemata.Abstractions.Exceptions;
using Schemata.Common;
using Schemata.Entity.Repository;
using Schemata.Mapping.Skeleton;
using Schemata.Resource.Foundation;
using Schemata.Resource.Foundation.Advisors;
using Schemata.Resource.Tests.Fixtures;
using Xunit;

namespace Schemata.Resource.Tests.ResourceOperationHandler;

public class OperationHandlerDeleteShould
{
    private readonly HandlerFixture _fixture = new();

    [Fact]
    public async Task Delete_ExistingEntity_RemovesAndCommits() {
        var handler = _fixture.CreateHandler();
        var entity  = _fixture.Students[0];
        var before  = _fixture.Students.Count;

        var result = await handler.DeleteAsync(entity.CanonicalName!, null, null, null);

        Assert.NotNull(result);
        Assert.Null(result.Detail);
        Assert.Equal(before - 1, _fixture.Students.Count);
        _fixture.Repository.Verify(r => r.CommitAsync(CancellationToken.None), Times.AtLeastOnce);
    }

    [Fact]
    public async Task Delete_MissingEntity_WithoutAllowMissing_ThrowsNotFound() {
        var handler = _fixture.CreateHandler();

        await Assert.ThrowsAsync<NotFoundException>(() => handler.DeleteAsync("students/missing", null, null, null));
    }

    [Fact]
    public async Task Delete_MissingEntity_WithAllowMissing_Succeeds() {
        var handler = _fixture.CreateHandler();
        var before  = _fixture.Students.Count;

        var result = await handler.DeleteAsync("students/missing", null, null, null, true);

        Assert.NotNull(result);
        Assert.Null(result.Detail);
        Assert.Equal(before, _fixture.Students.Count);
        _fixture.Repository.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Delete_ETagMismatch_ThrowsConcurrencyException() {
        var handler = _fixture.CreateHandler(services => {
            services.TryAddScoped<IResourceDeleteAdvisor<Student>, AdviceDeleteFreshness<Student>>();
        });
        var entity = _fixture.Students[0];

        await Assert.ThrowsAsync<ConcurrencyException>(() => handler.DeleteAsync(
                                                           entity.CanonicalName!, "W/\"wrongtag\"", null, null));
    }

    [Fact]
    public async Task Delete_SoftDeletableEntity_ReturnsUpdatedDetail() {
        var entity = new TrashStudent {
            Uid           = Identifiers.NewUid(),
            Name          = "alice-1",
            CanonicalName = "trashStudents/alice-1",
            Timestamp     = Identifiers.NewUid(),
        };

        var repository = new Mock<IRepository<TrashStudent>>();
        repository.Setup(r => r.SuppressQuerySoftDelete()).Returns(Mock.Of<IDisposable>());
        repository.Setup(r => r.SingleOrDefaultAsync<TrashStudent>(
                             It.IsAny<Func<IQueryable<TrashStudent>, IQueryable<TrashStudent>>>(),
                             It.IsAny<CancellationToken>()))
                  .ReturnsAsync(entity);
        // Soft-delete removal stamps DeleteTime for ISoftDelete entities.
        repository.Setup(r => r.RemoveAsync(It.IsAny<TrashStudent>(), It.IsAny<CancellationToken>()))
                  .Returns((TrashStudent e, CancellationToken _) => {
                       e.DeleteTime = DateTime.UtcNow;
                       return Task.CompletedTask;
                   });
        repository.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var mapper = new Mock<ISimpleMapper>();
        mapper.Setup(m => m.Map<TrashStudent, TrashStudent>(It.IsAny<TrashStudent>())).Returns<TrashStudent>(e => e);

        var services = new ServiceCollection();
        services.TryAddScoped<IResourceResponseAdvisor<TrashStudent, TrashStudent>,
            AdviceResponseFreshness<TrashStudent, TrashStudent>>();
        var sp = services.BuildServiceProvider();

        var handler = new ResourceOperationHandler<TrashStudent, TrashStudent, TrashStudent, TrashStudent>(
            sp, repository.Object, mapper.Object);

        var result = await handler.DeleteAsync(entity.CanonicalName!, null, null, null);

        Assert.NotNull(result.Detail);
        Assert.NotNull(result.Detail.DeleteTime);
        Assert.NotNull(result.Detail.EntityTag);
    }
}
