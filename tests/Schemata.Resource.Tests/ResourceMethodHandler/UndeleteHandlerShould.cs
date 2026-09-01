using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.Repository;
using Schemata.Mapping.Skeleton;
using Schemata.Resource.Foundation;
using Schemata.Resource.Foundation.Commands;
using Schemata.Resource.Tests.Fixtures;
using Xunit;

namespace Schemata.Resource.Tests.ResourceMethodHandler;

public class UndeleteHandlerShould
{
    [Fact]
    public async Task Invoke_SoftDeletedEntity_ClearsDeleteStateAndReturnsDetail() {
        var entity = new TrashStudent {
            Name          = "alice-1",
            CanonicalName = "trashStudents/alice-1",
            DeleteTime    = DateTime.UtcNow,
            PurgeTime     = DateTime.UtcNow.AddDays(7),
        };
        var other = new TrashStudent {
            Name          = "bob-1",
            CanonicalName = "trashStudents/bob-1",
            DeleteTime    = DateTime.UtcNow,
            PurgeTime     = DateTime.UtcNow.AddDays(7),
        };

        var suppression = new Mock<IDisposable>();
        var repository = new Mock<IRepository<TrashStudent>>();
        repository.Setup(r => r.SuppressQuerySoftDelete()).Returns(suppression.Object);
        repository.Setup(r => r.SingleOrDefaultAsync(
                             It.IsAny<Func<IQueryable<TrashStudent>, IQueryable<TrashStudent>>>(),
                             It.IsAny<CancellationToken>()))
                  .Returns((Func<IQueryable<TrashStudent>, IQueryable<TrashStudent>> query, CancellationToken _) =>
                      ValueTask.FromResult<TrashStudent?>(query(new[] { other, entity }.AsQueryable())
                                                         .SingleOrDefault()));
        repository.Setup(r => r.UpdateAsync(entity, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repository.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var mapper = new Mock<ISimpleMapper>();
        mapper.Setup(m => m.Map<TrashStudent, TrashStudent>(entity)).Returns(entity);
        var handler = new UndeleteHandler<TrashStudent, TrashStudent>(repository.Object, mapper.Object);

        var detail = await handler.HandleAsync(
            new UndeleteResourceRequest<TrashStudent, TrashStudent> { CanonicalName = entity.CanonicalName },
            CancellationToken.None);

        Assert.Same(entity, detail);
        Assert.Null(entity.DeleteTime);
        Assert.Null(entity.PurgeTime);
        repository.Verify(r => r.UpdateAsync(entity, CancellationToken.None), Times.Once);
        repository.Verify(r => r.CommitAsync(CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task Invoke_LiveEntity_ThrowsAlreadyExists() {
        var entity = new TrashStudent { Name = "alice-1", CanonicalName = "trashStudents/alice-1" };

        var suppression = new Mock<IDisposable>();
        var repository = new Mock<IRepository<TrashStudent>>();
        repository.Setup(r => r.SuppressQuerySoftDelete()).Returns(suppression.Object);
        repository.Setup(r => r.SingleOrDefaultAsync(
                             It.IsAny<Func<IQueryable<TrashStudent>, IQueryable<TrashStudent>>>(),
                             It.IsAny<CancellationToken>()))
                  .Returns(ValueTask.FromResult<TrashStudent?>(entity));
        var mapper  = new Mock<ISimpleMapper>();
        var handler = new UndeleteHandler<TrashStudent, TrashStudent>(repository.Object, mapper.Object);

        var ex = await Assert.ThrowsAsync<AlreadyExistsException>(() => handler.HandleAsync(
            new UndeleteResourceRequest<TrashStudent, TrashStudent> { CanonicalName = entity.CanonicalName },
            CancellationToken.None));

        var resource = Assert.Single(ex.Details!.OfType<ResourceInfoDetail>());
        Assert.Equal(entity.CanonicalName, resource.ResourceName);
        repository.Verify(r => r.UpdateAsync(It.IsAny<TrashStudent>(), It.IsAny<CancellationToken>()), Times.Never);
        repository.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
