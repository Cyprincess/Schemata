using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Schemata.Abstractions;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.Repository;
using Schemata.Mapping.Skeleton;
using Schemata.Resource.Foundation;
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

        var repository = new Mock<IRepository<TrashStudent>>();
        repository.Setup(r => r.UpdateAsync(entity, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repository.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var mapper = new Mock<ISimpleMapper>();
        mapper.Setup(m => m.Map<TrashStudent, TrashStudent>(entity)).Returns(entity);

        var handler = new UndeleteHandler<TrashStudent, TrashStudent>(repository.Object, mapper.Object);

        var detail = await handler.InvokeAsync(entity.CanonicalName, new(), entity, Mock.Of<ClaimsPrincipal>(),
                                               CancellationToken.None);

        Assert.Same(entity, detail);
        Assert.Null(entity.DeleteTime);
        Assert.Null(entity.PurgeTime);
        repository.Verify(r => r.UpdateAsync(entity, CancellationToken.None), Times.Once);
        repository.Verify(r => r.CommitAsync(CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task Invoke_LiveEntity_ThrowsFailedPrecondition() {
        var entity = new TrashStudent { Name = "alice-1", CanonicalName = "trashStudents/alice-1" };

        var repository = new Mock<IRepository<TrashStudent>>();
        var mapper     = new Mock<ISimpleMapper>();
        var handler    = new UndeleteHandler<TrashStudent, TrashStudent>(repository.Object, mapper.Object);

        var ex = await Assert.ThrowsAsync<FailedPreconditionException>(() => handler.InvokeAsync(
                                                                          entity.CanonicalName, new(), entity,
                                                                          null,
                                                                          CancellationToken.None)
                                                                     .AsTask());

        var resource = Assert.Single(ex.Details!.OfType<ResourceInfoDetail>());
        Assert.Equal(entity.CanonicalName, resource.ResourceName);
        var precondition = Assert.Single(ex.Details!.OfType<PreconditionFailureDetail>());
        var violation    = Assert.Single(precondition.Violations!);
        Assert.Equal(SchemataConstants.PreconditionSubjects.NotSoftDeleted, violation.Subject);
        repository.Verify(r => r.UpdateAsync(It.IsAny<TrashStudent>(), It.IsAny<CancellationToken>()), Times.Never);
        repository.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
