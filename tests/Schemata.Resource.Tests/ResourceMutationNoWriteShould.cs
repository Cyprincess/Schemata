using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.Repository;
using Schemata.Mapping.Skeleton;
using Schemata.Resource.Foundation;
using Schemata.Resource.Tests.Fixtures;
using Xunit;

namespace Schemata.Resource.Tests;

public class ResourceMutationNoWriteShould
{
    [Trait("Layer", "Unit")]
    [Fact]
    public async Task Create_NoWrite_ReturnsEmptyResult_WithoutMappingADetail() {
        var request = new Student { Name = "s1", CanonicalName = "students/s1" };
        var entity  = new Student { Name = "s1", CanonicalName = "students/s1" };

        var unit       = UnitOfWorkMock.Create();
        var repository = UnitOfWorkMock.Repository<Student>(unit);
        var mapper = new Mock<ISimpleMapper>(MockBehavior.Strict);
        mapper.Setup(m => m.Map<Student, Student>(request)).Returns(entity);

        var owner = new Mock<IResourceMutation<Student>>(MockBehavior.Strict);
        owner.Setup(o => o.CreateAsync(entity, It.IsAny<IUnitOfWork?>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(MutationResult.NoWrite);

        using var services  = new ServiceCollection().AddSingleton(owner.Object).BuildServiceProvider();
        var advice   = new AdviceContext(services);
        var       handler   = new ResourceOperationHandler<Student, Student, Student, Student>(services, repository.Object, mapper.Object);

        var result = await handler.CreateAsync(request, null, advice, CancellationToken.None);

        Assert.Null(result.Detail);
        owner.Verify(o => o.CreateAsync(entity, unit.Object, It.IsAny<CancellationToken>()), Times.Once);
        unit.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Trait("Layer", "Unit")]
    [Fact]
    public async Task Update_NoWrite_ReturnsEmptyResult_AndDropsTheMappedValues() {
        var entity  = new Student { Name = "s1", CanonicalName = "students/s1", FullName = "Original" };
        var request = new Student { Name = "s1", CanonicalName = "students/s1", FullName = "Renamed" };

        var (repository, unit) = LoadedRepository(entity);
        var mapper = new Mock<ISimpleMapper>(MockBehavior.Strict);
        mapper.Setup(m => m.Map<Student, Student>(request, entity))
              .Callback((Student source, Student destination) => destination.FullName = source.FullName);

        var owner = new Mock<IResourceMutation<Student>>(MockBehavior.Strict);
        owner.Setup(o => o.UpdateAsync(entity, It.IsAny<IUnitOfWork?>(), Operations.Update, It.IsAny<CancellationToken>()))
             .ReturnsAsync(MutationResult.NoWrite);

        using var services = new ServiceCollection().AddSingleton(owner.Object).BuildServiceProvider();
        var advice  = new AdviceContext(services);
        var       handler  = new ResourceOperationHandler<Student, Student, Student, Student>(services, repository.Object, mapper.Object);

        var result = await handler.UpdateAsync("students/s1", request, null, advice, CancellationToken.None);

        Assert.Null(result.Detail);
        Assert.Equal("Renamed", entity.FullName);
        owner.Verify(o => o.UpdateAsync(entity, unit.Object, Operations.Update, It.IsAny<CancellationToken>()), Times.Once);
        unit.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Trait("Layer", "Unit")]
    [Fact]
    public async Task Delete_NoWrite_ReturnsEmptyResult() {
        var entity = new Student { Name = "s1", CanonicalName = "students/s1" };

        var (repository, unit) = LoadedRepository(entity);
        var owner = new Mock<IResourceMutation<Student>>(MockBehavior.Strict);
        owner.Setup(o => o.DeleteAsync(entity, It.IsAny<IUnitOfWork?>(), Operations.Delete, It.IsAny<CancellationToken>()))
             .ReturnsAsync(MutationResult.NoWrite);

        using var services = new ServiceCollection().AddSingleton(owner.Object).BuildServiceProvider();
        var advice  = new AdviceContext(services);
        var       handler  = new ResourceOperationHandler<Student, Student, Student, Student>(services, repository.Object, Mock.Of<ISimpleMapper>());

        var result = await handler.DeleteAsync("students/s1", null, null, advice, CancellationToken.None);

        Assert.Null(result.Detail);
        owner.Verify(o => o.DeleteAsync(entity, unit.Object, Operations.Delete, It.IsAny<CancellationToken>()), Times.Once);
        unit.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Trait("Layer", "Unit")]
    [Fact]
    public async Task Delete_AlreadySoftDeleted_WithoutAllowMissing_ThrowsNotFound_WithoutRedeleting() {
        var deletedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var entity    = Tombstone(deletedAt);

        var (handler, owner, captured, provider) = HandlerFor(entity);
        using var services = provider;
        var advice = new AdviceContext(services);
        await Assert.ThrowsAsync<NotFoundException>(
            () => handler.DeleteAsync("trashStudents/t1", null, null, advice, CancellationToken.None));

        owner.Verify(o => o.DeleteAsync(
                         It.IsAny<TrashStudent>(), It.IsAny<IUnitOfWork?>(), It.IsAny<Operations>(),
                         It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(deletedAt, captured.DeleteTime);
    }

    [Trait("Layer", "Unit")]
    [Fact]
    public async Task Delete_AlreadySoftDeleted_WithAllowMissing_ReturnsEmpty_AndKeepsDeleteTime() {
        var deletedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var entity    = Tombstone(deletedAt);

        var (handler, owner, captured, provider) = HandlerFor(entity);
        using var services = provider;
        var advice = new AdviceContext(services);
        var result = await handler.DeleteAsync("trashStudents/t1", null, null, advice, CancellationToken.None, allowMissing: true);

        Assert.Null(result.Detail);
        owner.Verify(o => o.DeleteAsync(
                         It.IsAny<TrashStudent>(), It.IsAny<IUnitOfWork?>(), It.IsAny<Operations>(),
                         It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(deletedAt, captured.DeleteTime);
    }

    [Trait("Layer", "Unit")]
    [Fact]
    public async Task Delete_FirstSoftDelete_Mutates_AndReturnsTheTombstoneDetail() {
        var entity = new TrashStudent { Name = "t1", CanonicalName = "trashStudents/t1" };
        var detail = new TrashStudent { Name = "t1", CanonicalName = "trashStudents/t1" };

        var (repository, unit) = LoadedRepository(entity);
        var mappedBeforeCommit = false;
        var mapper = new Mock<ISimpleMapper>(MockBehavior.Strict);
        mapper.Setup(m => m.Map<TrashStudent, TrashStudent>(entity))
              .Callback(() => mappedBeforeCommit = !unit.Invocations.Any(invocation
                  => invocation.Method.Name == nameof(IUnitOfWork.CommitAsync)))
              .Returns(detail);

        var deletedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var owner = new Mock<IResourceMutation<TrashStudent>>(MockBehavior.Strict);
        owner.Setup(o => o.DeleteAsync(entity, It.IsAny<IUnitOfWork?>(), Operations.Delete, It.IsAny<CancellationToken>()))
             .Callback(() => entity.DeleteTime = deletedAt)
             .ReturnsAsync(MutationResult.Applied);

        using var services = new ServiceCollection().AddSingleton(owner.Object).BuildServiceProvider();
        var advice  = new AdviceContext(services);
        var       handler  = new ResourceOperationHandler<TrashStudent, TrashStudent, TrashStudent, TrashStudent>(
            services, repository.Object, mapper.Object);

        var result = await handler.DeleteAsync("trashStudents/t1", null, null, advice, CancellationToken.None);

        Assert.Same(detail, result.Detail);
        Assert.Equal(deletedAt, entity.DeleteTime);
        Assert.True(mappedBeforeCommit, "The tombstone detail must be mapped before the dispatch commits.");
        owner.Verify(o => o.DeleteAsync(entity, unit.Object, Operations.Delete, It.IsAny<CancellationToken>()), Times.Once);
        unit.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private static TrashStudent Tombstone(DateTime deletedAt) {
        return new() { Name = "t1", CanonicalName = "trashStudents/t1", DeleteTime = deletedAt };
    }

    private static (ResourceOperationHandler<TrashStudent, TrashStudent, TrashStudent, TrashStudent> Handler,
                    Mock<IResourceMutation<TrashStudent>> Owner, TrashStudent Entity, ServiceProvider Services) HandlerFor(TrashStudent entity) {
        var (repository, _) = LoadedRepository(entity);
        var owner      = new Mock<IResourceMutation<TrashStudent>>(MockBehavior.Strict);

        var services = new ServiceCollection().AddSingleton(owner.Object).BuildServiceProvider();
        var handler = new ResourceOperationHandler<TrashStudent, TrashStudent, TrashStudent, TrashStudent>(
            services, repository.Object, Mock.Of<ISimpleMapper>());

        return (handler, owner, entity, services);
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
