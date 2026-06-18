using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.Repository;
using Schemata.Resource.Foundation;
using Schemata.Resource.Tests.Fixtures;
using Xunit;

namespace Schemata.Resource.Tests.ResourceMethodHandler;

public class ExpungeHandlerShould
{
    [Fact]
    public async Task Invoke_SoftDeletedEntity_PhysicallyRemovesAndCommits() {
        var entity = new TrashStudent {
            Name          = "alice-1",
            CanonicalName = "trashStudents/alice-1",
            DeleteTime    = DateTime.UtcNow,
        };

        var suppression = new Mock<IDisposable>();
        var repository  = new Mock<IRepository<TrashStudent>>();
        repository.Setup(r => r.SuppressSoftDelete()).Returns(suppression.Object);
        repository.Setup(r => r.RemoveAsync(entity, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repository.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var handler = new ExpungeHandler<TrashStudent>(repository.Object);

        var response = await handler.InvokeAsync(entity.CanonicalName, new(), entity, Mock.Of<ClaimsPrincipal>(), CancellationToken.None);

        // AIP-164 expunge returns an empty body, not the resource identity.
        Assert.NotNull(response);
        repository.Verify(r => r.SuppressSoftDelete(), Times.Once);
        repository.Verify(r => r.RemoveAsync(entity, CancellationToken.None), Times.Once);
        repository.Verify(r => r.CommitAsync(CancellationToken.None), Times.Once);
        suppression.Verify(s => s.Dispose(), Times.Once);
    }

    [Fact]
    public async Task Invoke_LiveEntity_ThrowsFailedPreconditionException() {
        var entity = new TrashStudent {
            Name          = "alice-1",
            CanonicalName = "trashStudents/alice-1",
        };

        var repository = new Mock<IRepository<TrashStudent>>();
        var handler    = new ExpungeHandler<TrashStudent>(repository.Object);

        await Assert.ThrowsAsync<FailedPreconditionException>(() => handler.InvokeAsync(
            entity.CanonicalName, new(), entity, null, CancellationToken.None).AsTask());

        repository.Verify(r => r.SuppressSoftDelete(), Times.Never);
        repository.Verify(r => r.RemoveAsync(It.IsAny<TrashStudent>(), It.IsAny<CancellationToken>()), Times.Never);
        repository.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
