using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.Repository;
using Schemata.Resource.Foundation.Handlers;
using Schemata.Resource.Tests.Fixtures;
using Xunit;

namespace Schemata.Resource.Tests.ResourceMethodHandler;

public class ExpungeHandlerShould
{
    [Fact]
    public async Task Invoke_LiveEntity_ThrowsFailedPreconditionException() {
        var entity = new TrashStudent { Name = "alice-1", CanonicalName = "trashStudents/alice-1" };

        var repository = new Mock<IRepository<TrashStudent>>();
        repository.Setup(r => r.SuppressQuerySoftDelete()).Returns(Mock.Of<IDisposable>());
        repository.Setup(r => r.SingleOrDefaultAsync(
                             It.IsAny<Func<IQueryable<TrashStudent>, IQueryable<TrashStudent>>>(),
                             It.IsAny<CancellationToken>()))
                  .Returns(ValueTask.FromResult<TrashStudent?>(entity));
        var owner = ResourceMutationMock.Create<TrashStudent>();

        var handler = new ExpungeHandler<TrashStudent>(repository.Object, owner.Object);

        await Assert.ThrowsAsync<FailedPreconditionException>(() => handler.HandleAsync(
            new() { CanonicalName = entity.CanonicalName },
            CancellationToken.None));

        owner.Verify(o => o.DeleteAsync(
                         It.IsAny<TrashStudent>(),
                         It.IsAny<IUnitOfWork?>(),
                         It.IsAny<Operations>(),
                         It.IsAny<CancellationToken>()), Times.Never);
    }
}
