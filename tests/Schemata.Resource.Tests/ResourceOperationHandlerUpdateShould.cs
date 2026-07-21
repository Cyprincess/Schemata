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
    [Fact]
    public async Task MissingResource_WithAllowMissing_CreatesThroughCreateAndResponsePipelines() {
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
        repository.Setup(r => r.AddAsync(entity, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repository.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var mapper = new Mock<ISimpleMapper>(MockBehavior.Strict);
        mapper.Setup(m => m.Map<Student, Student>(request)).Returns(entity);
        mapper.Setup(m => m.Map<Student, Student>(entity)).Returns(detail);

        var createCalled = false;
        var responseCalled = false;
        Student? advisedEntity = null;
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
        var response = new Mock<IResourceResponseAdvisor<Student, Student>>();
        response.SetupGet(advisor => advisor.Order).Returns(0);
        response.Setup(advisor => advisor.AdviseAsync(
                      It.IsAny<AdviceContext>(),
                      It.IsAny<Student?>(),
                      It.IsAny<Student?>(),
                      It.IsAny<ClaimsPrincipal?>(),
                      It.IsAny<CancellationToken>()))
                .Callback((AdviceContext context, Student? entity, Student? detail, ClaimsPrincipal? principal, CancellationToken cancellationToken) => {
                    responseCalled = true;
                    advisedEntity = entity;
                })
                .Returns(Task.FromResult(AdviseResult.Continue));
        using var services = Services<Student, Student, Student>(create: create.Object, response: response.Object);
        var handler = new ResourceOperationHandler<Student, Student, Student, Student>(
            services, repository.Object, mapper.Object);

        var result = await handler.UpdateAsync("students/missing", request, null, CancellationToken.None);

        Assert.True(createCalled);
        Assert.True(responseCalled);
        Assert.Same(entity, advisedEntity);
        Assert.Same(detail, result.Detail);
        repository.Verify(r => r.AddAsync(entity, CancellationToken.None), Times.Once);
        repository.Verify(r => r.CommitAsync(CancellationToken.None), Times.Once);
        mapper.Verify(m => m.Map<Student, Student>(request, It.IsAny<Student>(), It.IsAny<IEnumerable<string>>()), Times.Never);
    }

    [Fact]
    public async Task MissingChildResource_WithAllowMissing_AppliesLeafAndParentPredicatesToCreateContainer() {
        var request = new Widget { Name = "w1", AllowMissing = true };
        var entity  = new Widget { Name = "w1", Tenant = "acme" };
        var detail  = new Widget { Name = "w1", Tenant = "acme" };

        var repository = MissingRepository<Widget>();
        repository.Setup(r => r.AddAsync(entity, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repository.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

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

        using var services = Services<Widget, Widget, Widget>(create: create.Object);
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
    }

    [Fact]
    public async Task MissingResource_WithAllowMissing_RunsCreatePipelineInCreateOrder() {
        var request = new Student { Name = "missing", AllowMissing = true };
        var entity  = new Student { Name = "missing" };
        var detail  = new Student { Name = "missing" };

        var repository = MissingRepository<Student>();
        repository.Setup(r => r.AddAsync(entity, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repository.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

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

        using var services = Services<Student, Student, Student>(create: createRequest.Object, createEntity: create.Object);
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
        repository.Verify(r => r.AddAsync(entity, CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task MissingResource_WithoutAllowMissing_ThrowsNotFound() {
        var repository = MissingRepository<Student>();
        var mapper = new Mock<ISimpleMapper>(MockBehavior.Strict);
        using var services = Services<Student, Student, Student>();
        var handler = new ResourceOperationHandler<Student, Student, Student, Student>(
            services, repository.Object, mapper.Object);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.UpdateAsync(
            "students/missing", new Student { AllowMissing = false }, null, CancellationToken.None));
    }

    [Fact]
    public async Task MissingResource_WithoutAllowMissingContract_ThrowsNotFound() {
        var repository = MissingRepository<Student>();
        var mapper = new Mock<ISimpleMapper>(MockBehavior.Strict);
        using var services = Services<Student, RequestWithoutAllowMissing, Student>();
        var handler = new ResourceOperationHandler<Student, RequestWithoutAllowMissing, Student, Student>(
            services, repository.Object, mapper.Object);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.UpdateAsync(
            "students/missing", new RequestWithoutAllowMissing(), null, CancellationToken.None));
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
        IResourceCreateRequestAdvisor<TEntity, TRequest>? create = null,
        IResourceCreateAdvisor<TEntity, TRequest>?        createEntity = null,
        IResourceResponseAdvisor<TEntity, TDetail>?       response = null
    )
        where TEntity : class, ICanonicalName
        where TRequest : class, ICanonicalName
        where TDetail : class, ICanonicalName
    {
        var services = new ServiceCollection();
        if (create is not null) {
            services.AddSingleton(create);
        }

        if (createEntity is not null) {
            services.AddSingleton(createEntity);
        }

        if (response is not null) {
            services.AddSingleton(response);
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
