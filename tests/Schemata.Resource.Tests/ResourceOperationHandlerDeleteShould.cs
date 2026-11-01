using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Entity.Repository;
using Schemata.Mapping.Skeleton;
using Schemata.Resource.Foundation;
using Schemata.Resource.Tests.Fixtures;
using Xunit;

namespace Schemata.Resource.Tests;

public class ResourceOperationHandlerDeleteShould
{
    [Trait("Layer", "Unit")]
    [Fact]
    public async Task Delete_WhenTombstoneMappingThrows_DoesNotCommitTheRemoval() {
        var entity = new TrashStudent { Name = "t1", CanonicalName = "trashStudents/t1" };

        var (repository, unit) = LoadedRepository(entity);
        var deletedAt   = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var owner = new Mock<IResourceMutation<TrashStudent>>(MockBehavior.Strict);
        owner.Setup(o => o.DeleteAsync(entity, It.IsAny<IUnitOfWork?>(), Operations.Delete, It.IsAny<CancellationToken>()))
             .Callback(() => entity.DeleteTime = deletedAt)
             .ReturnsAsync(MutationResult.Applied);

        var mappedBeforeCommit = false;
        var mapper = new Mock<ISimpleMapper>(MockBehavior.Strict);
        mapper.Setup(m => m.Map<TrashStudent, TrashStudent>(entity))
              .Callback(() => mappedBeforeCommit = !unit.Invocations.Any(invocation
                  => invocation.Method.Name == nameof(IUnitOfWork.CommitAsync)))
              .Throws(new InvalidOperationException("detail mapping failed"));

        using var services = new ServiceCollection().AddSingleton(owner.Object).BuildServiceProvider();
        var advice  = new AdviceContext(services);
        var handler = new ResourceOperationHandler<TrashStudent, TrashStudent, TrashStudent, TrashStudent>(
            services, repository.Object, mapper.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.DeleteAsync("trashStudents/t1", null, null, advice, CancellationToken.None));

        Assert.True(mappedBeforeCommit, "The tombstone detail must be mapped before the dispatch commits.");
        owner.Verify(o => o.DeleteAsync(entity, unit.Object, Operations.Delete, It.IsAny<CancellationToken>()), Times.Once);
        unit.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Trait("Layer", "Unit")]
    [Fact]
    public async Task Delete_HardDelete_CommitsWithoutMappingADetail() {
        var entity = new Student { Name = "s1", CanonicalName = "students/s1" };

        var (repository, unit) = LoadedRepository(entity);
        var owner = new Mock<IResourceMutation<Student>>(MockBehavior.Strict);
        owner.Setup(o => o.DeleteAsync(entity, It.IsAny<IUnitOfWork?>(), Operations.Delete, It.IsAny<CancellationToken>()))
             .ReturnsAsync(MutationResult.Applied);

        // A hard delete has no tombstone detail to project: a mapper that would throw on any call
        // proves the handler never maps one.
        var mapper = new Mock<ISimpleMapper>(MockBehavior.Strict);

        using var services = new ServiceCollection().AddSingleton(owner.Object).BuildServiceProvider();
        var advice  = new AdviceContext(services);
        var handler = new ResourceOperationHandler<Student, Student, Student, Student>(
            services, repository.Object, mapper.Object);

        var result = await handler.DeleteAsync("students/s1", null, null, advice, CancellationToken.None);

        Assert.Null(result.Detail);
        owner.Verify(o => o.DeleteAsync(entity, unit.Object, Operations.Delete, It.IsAny<CancellationToken>()), Times.Once);
        unit.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private static (Mock<IRepository<TEntity>> Repository, Mock<IUnitOfWork> Unit) LoadedRepository<TEntity>(TEntity entity)
        where TEntity : class, ICanonicalName {
        var unit       = UnitOfWorkMock.Create();
        var repository = UnitOfWorkMock.Repository<TEntity>(unit);
        repository.Setup(r => r.SingleOrDefaultAsync(
                              It.IsAny<Func<IQueryable<TEntity>, IQueryable<TEntity>>>(),
                              It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()))
                  .Returns(new ValueTask<TEntity?>(entity));
        return (repository, unit);
    }
}
