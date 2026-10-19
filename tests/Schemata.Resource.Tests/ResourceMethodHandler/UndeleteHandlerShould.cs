using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.Repository;
using Schemata.Mapping.Skeleton;
using Schemata.Resource.Foundation;
using Schemata.Resource.Tests.Fixtures;
using Xunit;

using Schemata.Abstractions.Advisors;
using Microsoft.Extensions.DependencyInjection;
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

        var repository = new Mock<IRepository<TrashStudent>>();
        repository.Setup(r => r.SingleOrDefaultAsync(
                             It.IsAny<Func<IQueryable<TrashStudent>, IQueryable<TrashStudent>>>(),
                             It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()))
                  .Returns((Func<IQueryable<TrashStudent>, IQueryable<TrashStudent>> query, CancellationToken _, QueryOptions _) =>
                      ValueTask.FromResult<TrashStudent?>(query(new[] { other, entity }.AsQueryable())
                                                         .SingleOrDefault()));
        var owner = ResourceMutationMock.Create<TrashStudent>();

        var mapper = new Mock<ISimpleMapper>();
        mapper.Setup(m => m.Map<TrashStudent, TrashStudent>(entity)).Returns(entity);
        var handler = new UndeleteHandler<TrashStudent, TrashStudent>(repository.Object, owner.Object, mapper.Object);

        var detail = await handler.HandleAsync(
            new() { CanonicalName = entity.CanonicalName }, EmptyAdvice(),
            CancellationToken.None);

        Assert.Same(entity, detail);
        Assert.Null(entity.DeleteTime);
        Assert.Null(entity.PurgeTime);
        repository.Verify(r => r.UpdateAsync(It.IsAny<TrashStudent>(), It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>()), Times.Never);
        repository.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Invoke_LiveEntity_ThrowsAlreadyExists() {
        var entity = new TrashStudent { Name = "alice-1", CanonicalName = "trashStudents/alice-1" };

        var repository = new Mock<IRepository<TrashStudent>>();
        repository.Setup(r => r.SingleOrDefaultAsync(
                             It.IsAny<Func<IQueryable<TrashStudent>, IQueryable<TrashStudent>>>(),
                             It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()))
                  .Returns(ValueTask.FromResult<TrashStudent?>(entity));
        var owner = ResourceMutationMock.Create<TrashStudent>();

        var mapper  = new Mock<ISimpleMapper>();
        var handler = new UndeleteHandler<TrashStudent, TrashStudent>(repository.Object, owner.Object, mapper.Object);

        var ex = await Assert.ThrowsAsync<AlreadyExistsException>(() => handler.HandleAsync(
            new() { CanonicalName = entity.CanonicalName }, EmptyAdvice(),
            CancellationToken.None));

        var resource = Assert.Single(ex.Details!.OfType<ResourceInfoDetail>());
        Assert.Equal(entity.CanonicalName, resource.ResourceName);
        owner.Verify(o => o.UpdateAsync(
                         It.IsAny<TrashStudent>(),
                         It.IsAny<IUnitOfWork?>(),
                         It.IsAny<Operations>(),
                         It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Invoke_NoWriteMutation_Returns_No_Restore_Detail() {
        var entity = new TrashStudent {
            Name          = "alice-1",
            CanonicalName = "trashStudents/alice-1",
            DeleteTime    = DateTime.UtcNow,
            PurgeTime     = DateTime.UtcNow.AddDays(7),
        };

        var repository = new Mock<IRepository<TrashStudent>>();
        repository.Setup(r => r.SingleOrDefaultAsync(
                             It.IsAny<Func<IQueryable<TrashStudent>, IQueryable<TrashStudent>>>(),
                             It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()))
                  .Returns(ValueTask.FromResult<TrashStudent?>(entity));
        var owner = ResourceMutationMock.Create<TrashStudent>();
        owner.Setup(o => o.UpdateAsync(
                    It.IsAny<TrashStudent>(),
                    It.IsAny<IUnitOfWork?>(),
                    It.IsAny<Operations>(),
                    It.IsAny<CancellationToken>()))
             .ReturnsAsync(MutationResult.NoWrite);

        var mapper = new Mock<ISimpleMapper>();
        var handler = new UndeleteHandler<TrashStudent, TrashStudent>(repository.Object, owner.Object, mapper.Object);

        var detail = await handler.HandleAsync(
            new() { CanonicalName = entity.CanonicalName }, EmptyAdvice(),
            CancellationToken.None);

        Assert.Null(detail);
        mapper.Verify(m => m.Map<TrashStudent, TrashStudent>(It.IsAny<TrashStudent>()), Times.Never);
    }

    private static AdviceContext EmptyAdvice() => new(new ServiceCollection().BuildServiceProvider());
}
