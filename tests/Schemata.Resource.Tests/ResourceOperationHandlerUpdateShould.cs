using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Common;
using Schemata.Resource.Foundation;
using Schemata.Resource.Foundation.Advisors;
using Schemata.Resource.Tests.Fixtures;
using Schemata.Entity.Repository;
using Schemata.Mapping.Skeleton;
using Xunit;

namespace Schemata.Resource.Tests;

public class ResourceOperationHandlerUpdateShould
{
    [Trait("Layer", "Unit")]
    [Fact]
    public async Task AllowMissing_Creates_Through_Mutation_Owner_Without_Repository_Writes() {
        var request = new Student { Name = "missing", CanonicalName = "students/missing", AllowMissing = true };
        var prepared = new Student { Name = "missing", CanonicalName = "students/missing" };
        var repository = MissingRepository<Student>();
        var mapper = new Mock<ISimpleMapper>(MockBehavior.Strict);
        mapper.Setup(m => m.Map<Student, Student>(request)).Returns(prepared);
        mapper.Setup(m => m.Map<Student, Student>(prepared)).Returns(prepared);
        var owner = new Mock<IResourceMutation<Student>>(MockBehavior.Strict);
        owner.Setup(o => o.CreateAsync(prepared, It.IsAny<IUnitOfWork?>(), It.IsAny<CancellationToken>()))
             .Callback((Student entity, IUnitOfWork? _, CancellationToken _) => entity.FullName = "Domain result")
             .ReturnsAsync(MutationResult.Applied);
        using var services = new ServiceCollection().AddSingleton(owner.Object).BuildServiceProvider();
        using var ambient = AdviceContext.Establish(new(services));
        var handler = new ResourceOperationHandler<Student, Student, Student, Student>(services, repository.Object, mapper.Object);

        var result = await handler.UpdateAsync("students/missing", request, null, CancellationToken.None);

        Assert.Equal("Domain result", result.Detail!.FullName);
        repository.Verify(r => r.AddAsync(It.IsAny<Student>(), It.IsAny<CancellationToken>()), Times.Never);
        repository.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MissingResource_WithAllowMissing_CreatesThroughTheCreatePipeline() {
        var request = new Student {
            Name = "missing",
            CanonicalName = "students/missing",
            FullName = "Created",
            UpdateMask = "full_name",
            AllowMissing = true,
        };
        var entity = new Student { Name = "missing", CanonicalName = "students/missing" };
        var detail = new Student { Name = "missing", CanonicalName = "students/missing" };

        var repository = MissingRepository<Student>();
        var owner = ResourceMutationMock.Create<Student>();

        var mapper = new Mock<ISimpleMapper>(MockBehavior.Strict);
        mapper.Setup(m => m.Map<Student, Student>(request)).Returns(entity);
        mapper.Setup(m => m.Map<Student, Student>(entity)).Returns(detail);

        var createCalled = false;
        var create = new Mock<IResourceCreateRequestAdvisor<Student, Student>>();
        create.SetupGet(advisor => advisor.Order).Returns(0);
        create.Setup(advisor => advisor.AdviseAsync(
                    It.IsAny<AdviceContext>(),
                    It.IsAny<Student>(),
                    It.IsAny<ResourceRequestContainer<Student>>(),
                    It.IsAny<ClaimsPrincipal?>(),
                    It.IsAny<CancellationToken>()))
              .Callback(() => createCalled = true)
              .Returns(Task.FromResult(AdviseResult.Continue));
        using var services = Services<Student, Student, Student>(owner, create: create.Object);
        using var ambient = AdviceContext.Establish(new(services));
        var handler = new ResourceOperationHandler<Student, Student, Student, Student>(
            services, repository.Object, mapper.Object);

        var result = await handler.UpdateAsync("students/missing", request, null, CancellationToken.None);

        Assert.True(createCalled);
        Assert.Same(detail, result.Detail);
        owner.Verify(o => o.CreateAsync(entity, null, CancellationToken.None), Times.Once);
        repository.Verify(r => r.AddAsync(It.IsAny<Student>(), It.IsAny<CancellationToken>()), Times.Never);
        repository.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
        mapper.Verify(m => m.Map<Student, Student>(request, It.IsAny<Student>(), It.IsAny<IEnumerable<string>>()), Times.Never);
    }

    [Fact]
    public async Task MissingChildResource_WithAllowMissing_AppliesLeafAndParentPredicatesToCreateContainer() {
        var request = new Widget { Name = "w1", AllowMissing = true };
        var entity  = new Widget { Name = "w1", Tenant = "acme" };
        var detail  = new Widget { Name = "w1", Tenant = "acme" };

        var repository = MissingRepository<Widget>();
        var owner = ResourceMutationMock.Create<Widget>();

        var mapper = new Mock<ISimpleMapper>(MockBehavior.Strict);
        mapper.Setup(m => m.Map<Widget, Widget>(request)).Returns(entity);
        mapper.Setup(m => m.Map<Widget, Widget>(entity)).Returns(detail);

        ResourceRequestContainer<Widget>? captured = null;
        var create = new Mock<IResourceCreateRequestAdvisor<Widget, Widget>>();
        create.SetupGet(advisor => advisor.Order).Returns(0);
        create.Setup(advisor => advisor.AdviseAsync(
                     It.IsAny<AdviceContext>(),
                     It.IsAny<Widget>(),
                     It.IsAny<ResourceRequestContainer<Widget>>(),
                     It.IsAny<ClaimsPrincipal?>(),
                     It.IsAny<CancellationToken>()))
              .Callback((AdviceContext context, Widget request, ResourceRequestContainer<Widget> container, ClaimsPrincipal? principal, CancellationToken cancellationToken) => {
                   captured = container;
               })
              .Returns(Task.FromResult(AdviseResult.Continue));

        using var services = Services<Widget, Widget, Widget>(owner, create: create.Object);
        using var ambient = AdviceContext.Establish(new(services));
        var handler = new ResourceOperationHandler<Widget, Widget, Widget, Widget>(
            services, repository.Object, mapper.Object);

        var result = await handler.UpdateAsync("tenants/acme/widgets/w1", request, null, CancellationToken.None);

        Assert.Same(detail, result.Detail);
        Assert.NotNull(captured);

        var sample = new[] {
            new Widget { Name = "w1", Tenant = "acme" },
            new Widget { Name = "w1", Tenant = "other" },
            new Widget { Name = "w2", Tenant = "acme" },
        }.AsQueryable();

        var match = Assert.Single(captured!.Query(sample));
        Assert.Equal("w1", match.Name);
        Assert.Equal("acme", match.Tenant);
        owner.Verify(o => o.CreateAsync(entity, null, CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task MissingResource_WithAllowMissing_RunsCreatePipelineInCreateOrder() {
        var request = new Student { Name = "missing", AllowMissing = true };
        var entity  = new Student { Name = "missing" };
        var detail  = new Student { Name = "missing" };

        var repository = MissingRepository<Student>();
        var owner = ResourceMutationMock.Create<Student>();

        var sequence = new MockSequence();

        var createRequest = new Mock<IResourceCreateRequestAdvisor<Student, Student>>(MockBehavior.Strict);
        createRequest.SetupGet(advisor => advisor.Order).Returns(0);
        createRequest.InSequence(sequence)
                     .Setup(advisor => advisor.AdviseAsync(
                              It.IsAny<AdviceContext>(),
                              request,
                              It.IsAny<ResourceRequestContainer<Student>>(),
                              It.IsAny<ClaimsPrincipal?>(),
                              It.IsAny<CancellationToken>()))
                     .Returns(Task.FromResult(AdviseResult.Continue));

        var mapper = new Mock<ISimpleMapper>(MockBehavior.Strict);
        mapper.InSequence(sequence)
              .Setup(m => m.Map<Student, Student>(request))
              .Returns(entity);
        mapper.Setup(m => m.Map<Student, Student>(entity)).Returns(detail);

        var create = new Mock<IResourceCreateAdvisor<Student, Student>>(MockBehavior.Strict);
        create.SetupGet(advisor => advisor.Order).Returns(0);
        create.InSequence(sequence)
              .Setup(advisor => advisor.AdviseAsync(
                       It.IsAny<AdviceContext>(),
                       request,
                       entity,
                       It.IsAny<ClaimsPrincipal?>(),
                       It.IsAny<CancellationToken>()))
              .Returns(Task.FromResult(AdviseResult.Continue));

        using var services = Services<Student, Student, Student>(owner, create: createRequest.Object, createEntity: create.Object);
        using var ambient = AdviceContext.Establish(new(services));
        var handler = new ResourceOperationHandler<Student, Student, Student, Student>(
            services, repository.Object, mapper.Object);

        var result = await handler.UpdateAsync("students/missing", request, null, CancellationToken.None);

        Assert.Same(detail, result.Detail);
        createRequest.Verify(advisor => advisor.AdviseAsync(
                                 It.IsAny<AdviceContext>(),
                                 request,
                                 It.IsAny<ResourceRequestContainer<Student>>(),
                                 It.IsAny<ClaimsPrincipal?>(),
                                 It.IsAny<CancellationToken>()), Times.Once);
        mapper.Verify(m => m.Map<Student, Student>(request), Times.Once);
        create.Verify(advisor => advisor.AdviseAsync(
                          It.IsAny<AdviceContext>(),
                          request,
                          entity,
                          It.IsAny<ClaimsPrincipal?>(),
                          It.IsAny<CancellationToken>()), Times.Once);
        owner.Verify(o => o.CreateAsync(entity, null, CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task MissingResource_WithoutAllowMissing_ThrowsNotFound() {
        var repository = MissingRepository<Student>();
        var mapper = new Mock<ISimpleMapper>(MockBehavior.Strict);
        using var services = Services<Student, Student, Student>();
        using var ambient = AdviceContext.Establish(new(services));
        var handler = new ResourceOperationHandler<Student, Student, Student, Student>(
            services, repository.Object, mapper.Object);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.UpdateAsync(
            "students/missing", new() { AllowMissing = false }, null, CancellationToken.None));
    }

    [Fact]
    public async Task MissingResource_WithoutAllowMissingContract_ThrowsNotFound() {
        var repository = MissingRepository<Student>();
        var mapper = new Mock<ISimpleMapper>(MockBehavior.Strict);
        using var services = Services<Student, RequestWithoutAllowMissing, Student>();
        using var ambient = AdviceContext.Establish(new(services));
        var handler = new ResourceOperationHandler<Student, RequestWithoutAllowMissing, Student, Student>(
            services, repository.Object, mapper.Object);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.UpdateAsync(
            "students/missing", new(), null, CancellationToken.None));
    }

    [Trait("Layer", "Unit")]
    [Fact]
    public async Task CreateCoreAsync_Writes_Through_Mutation_Owner_Without_Repository_Commit() {
        var request = new Student { Name = "s1" };
        var detail  = new Student { Name = "s1", CanonicalName = "students/s1" };

        var repository = new Mock<IRepository<Student>>();
        var owner = ResourceMutationMock.Create<Student>();

        var mapper = new Mock<ISimpleMapper>();
        mapper.Setup(m => m.Map<Student, Student>(It.IsAny<Student>())).Returns(detail);

        using var services = Services<Student, Student, Student>(owner);
        using var ambient = AdviceContext.Establish(new(services));
        var handler = new ResourceOperationHandler<Student, Student, Student, Student>(
            services, repository.Object, mapper.Object);

        var ctx = AdviceContext.Require();
        var result = await handler.CreateCoreAsync(ctx, request, null, CancellationToken.None);

        Assert.Same(detail, result.Detail);
        owner.Verify(o => o.CreateAsync(It.IsAny<Student>(), null, CancellationToken.None), Times.Once);
        repository.Verify(r => r.AddAsync(It.IsAny<Student>(), It.IsAny<CancellationToken>()), Times.Never);
        repository.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NullDetailMapping_ThrowsInvalidOperation() {
        var request = new Student { Name = "s1" };
        var entity  = new Student { Name = "s1", CanonicalName = "students/s1" };

        var repository = new Mock<IRepository<Student>>();
        repository.Setup(r => r.SuppressQuerySoftDelete()).Returns(Mock.Of<IDisposable>());
        repository.Setup(r => r.SingleOrDefaultAsync(
                       It.IsAny<Func<IQueryable<Student>, IQueryable<Student>>>(),
                       It.IsAny<CancellationToken>()))
                  .Returns(new ValueTask<Student?>(entity));
        var owner = ResourceMutationMock.Create<Student>();

        var mapper = new Mock<ISimpleMapper>(MockBehavior.Strict);
        mapper.Setup(m => m.Map<Student, Student>(request, entity));
        mapper.Setup(m => m.Map<Student, Student>(entity)).Returns((Student?)null);

        using var services = Services<Student, Student, Student>(owner);
        using var ambient  = AdviceContext.Establish(new(services));
        var handler = new ResourceOperationHandler<Student, Student, Student, Student>(
            services, repository.Object, mapper.Object);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.UpdateAsync("students/s1", request, null, CancellationToken.None));

        Assert.Contains(typeof(Student).FullName!, ex.Message);
        owner.Verify(o => o.UpdateAsync(entity, null, Operations.Update, CancellationToken.None), Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("*")]
    public async Task OmittedOrWildcardMask_Preserves_Loaded_System_Fields(string? mask) {
        var uid       = Guid.NewGuid();
        var timestamp = Guid.NewGuid();
        var entity = new Student {
            Name = "s1", CanonicalName = "students/s1", Uid = uid, Timestamp = timestamp, Age = 7,
        };
        var request = new Student { FullName = "Renamed", UpdateMask = mask };

        var repository = LoadedRepository(entity);
        var owner      = ResourceMutationMock.Create<Student>();
        var mapper     = MergingMapper();

        using var services = Services<Student, Student, Student>(owner);
        using var ambient  = AdviceContext.Establish(new(services));
        var handler = new ResourceOperationHandler<Student, Student, Student, Student>(
            services, repository.Object, mapper.Object);

        var result = await handler.UpdateAsync("students/s1", request, null, CancellationToken.None);

        Assert.Equal(uid, entity.Uid);
        Assert.Equal(timestamp, entity.Timestamp);
        Assert.Equal("s1", entity.Name);
        Assert.Equal("Renamed", entity.FullName);
        Assert.Same(entity, result.Detail);
        owner.Verify(o => o.UpdateAsync(entity, null, Operations.Update, CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task ExplicitMask_Preserves_System_Fields_Named_In_The_Mask() {
        var uid       = Guid.NewGuid();
        var timestamp = Guid.NewGuid();
        var entity = new Student {
            Name = "s1", CanonicalName = "students/s1", Uid = uid, Timestamp = timestamp,
        };
        var request = new Student { FullName = "Renamed", UpdateMask = "uid,timestamp,full_name" };

        var repository = LoadedRepository(entity);
        var owner      = ResourceMutationMock.Create<Student>();
        var mapper     = MergingMapper();

        using var services = Services<Student, Student, Student>(owner);
        using var ambient  = AdviceContext.Establish(new(services));
        var handler = new ResourceOperationHandler<Student, Student, Student, Student>(
            services, repository.Object, mapper.Object);

        await handler.UpdateAsync("students/s1", request, null, CancellationToken.None);

        Assert.Equal(uid, entity.Uid);
        Assert.Equal(timestamp, entity.Timestamp);
        Assert.Equal("Renamed", entity.FullName);
    }

    private static Mock<IRepository<Student>> LoadedRepository(Student entity) {
        var repository = new Mock<IRepository<Student>>();
        repository.Setup(r => r.SuppressQuerySoftDelete()).Returns(Mock.Of<IDisposable>());
        repository.Setup(r => r.SingleOrDefaultAsync(
                              It.IsAny<Func<IQueryable<Student>, IQueryable<Student>>>(),
                              It.IsAny<CancellationToken>()))
                  .Returns(new ValueTask<Student?>(entity));
        return repository;
    }

    // Mirrors the adapters' shared SimpleMapperHelper.MapMerging: a populated source member
    // (including a value-type default like Guid.Empty) overwrites the destination.
    private static Mock<ISimpleMapper> MergingMapper() {
        var mapper = new Mock<ISimpleMapper>();
        mapper.Setup(m => m.Map<Student, Student>(It.IsAny<Student>(), It.IsAny<Student>()))
              .Callback((Student source, Student destination) => MergePopulated(source, destination, null));
        mapper.Setup(m => m.Map<Student, Student>(
                   It.IsAny<Student>(), It.IsAny<Student>(), It.IsAny<IEnumerable<string>>()))
              .Callback((Student source, Student destination, IEnumerable<string> fields)
                         => MergePopulated(source, destination, fields.ToHashSet()));
        mapper.Setup(m => m.Map<Student, Student>(It.IsAny<Student>())).Returns((Student source) => source);
        return mapper;
    }

    private static void MergePopulated(Student source, Student destination, ISet<string>? fields) {
        foreach (var property in typeof(Student).GetProperties()) {
            if (!property.CanWrite || fields is not null && !fields.Contains(property.Name)) {
                continue;
            }

            var value = property.GetValue(source);
            if (value is null || value is string text && string.IsNullOrWhiteSpace(text)) {
                continue;
            }

            property.SetValue(destination, value);
        }
    }

    private static Mock<IRepository<T>> MissingRepository<T>() where T : class {
        var repository = new Mock<IRepository<T>>();
        repository.Setup(r => r.SuppressQuerySoftDelete()).Returns(Mock.Of<IDisposable>());
        repository.Setup(r => r.SingleOrDefaultAsync(
                              It.IsAny<Func<IQueryable<T>, IQueryable<T>>>(),
                              It.IsAny<CancellationToken>()))
                  .Returns(new ValueTask<T?>((T?)null));
        return repository;
    }

    private static ServiceProvider Services<TEntity, TRequest, TDetail>(
        Mock<IResourceMutation<TEntity>>?                 mutation = null,
        IResourceCreateRequestAdvisor<TEntity, TRequest>? create = null,
        IResourceCreateAdvisor<TEntity, TRequest>?        createEntity = null
    )
        where TEntity : class, ICanonicalName
        where TRequest : class, ICanonicalName
        where TDetail : class, ICanonicalName
    {
        var services = new ServiceCollection();
        if (mutation is not null) {
            services.AddSingleton(mutation.Object);
        }

        if (create is not null) {
            services.AddSingleton(create);
        }

        if (createEntity is not null) {
            services.AddSingleton(createEntity);
        }

        return services.BuildServiceProvider();
    }

    private sealed class RequestWithoutAllowMissing : ICanonicalName
    {
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }

    [CanonicalName("tenants/{tenant}/widgets/{widget}")]
    public sealed class Widget : ICanonicalName, IAllowMissing
    {
        public string? Tenant { get; set; }

        public bool AllowMissing { get; set; }

        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }
}
