using System;
using System.Collections.Generic;
using System.Linq;
using Schemata.Abstractions.Resource;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.Repository;
using Schemata.Mapping.Skeleton;
using Schemata.Resource.Foundation;
using Schemata.Resource.Foundation.Advisors;
using Schemata.Resource.Tests.Fixtures;
using Schemata.Security.Foundation;
using Schemata.Security.Skeleton;
using Xunit;

namespace Schemata.Resource.Tests;

public sealed class ResourceCreateAccessPipelineShould
{
    [Theory]
    [InlineData("denied", false, false)]
    [InlineData("allowed", true, false)]
    [InlineData("denied", false, true)]
    [InlineData("allowed", true, true)]
    public async Task Create_AuthorizesFinalStructuralParent_BeforePersistence(string parent, bool allowed, bool upsert) {
        var request = new Child { Name = "c1", Parent = $"parents/{parent}", ParentId = "allowed" };
        var entity = new Child { Name = "c1", ParentId = "allowed" };
        var unit       = UnitOfWorkMock.Create();
        var repository = new Mock<IRepository<Child>>(MockBehavior.Strict);
        repository.Setup(r => r.Begin()).Returns(unit.Object);
        if (upsert) {
            repository.Setup(r => r.SingleOrDefaultAsync(It.IsAny<Func<IQueryable<Child>, IQueryable<Child>>>(), It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()))
                .Returns(new ValueTask<Child?>((Child?)null));
        }
        var owner = ResourceMutationMock.Create<Child>();
        var mapper = new Mock<ISimpleMapper>(MockBehavior.Strict);
        mapper.Setup(m => m.Map<Child, Child>(request)).Returns(entity);
        if (allowed) mapper.Setup(m => m.Map<Child, Child>(entity)).Returns(entity);
        string? observed = null;
        var access = new Mock<IAccessProvider<Child, Child>>(MockBehavior.Strict);
        access.Setup(a => a.HasAccessAsync(entity, It.IsAny<AccessContext<Child>>(), null, It.IsAny<CancellationToken>()))
            .Returns((Child value, AccessContext<Child> _, ClaimsPrincipal? _, CancellationToken _) => {
                observed = value.ParentId;
                return Task.FromResult(value.ParentId == "allowed" ? AccessDecision.Allowed : AccessDecision.Denied);
            });
        using var services = new ServiceCollection().AddSingleton(access.Object)
            .AddSingleton(owner.Object)
            .AddSingleton<IPermissionResolver, DefaultPermissionResolver>()
            .AddSingleton<IResourceCreateAdvisor<Child, Child, Child>, ResourceCreateAccessAdvisor<Child, Child, Child>>()
            .BuildServiceProvider();
        var advice = new AdviceContext(services);
        var handler = new ResourceOperationHandler<Child, Child, Child, Child>(services, repository.Object, mapper.Object);

        if (allowed) {
            var result = upsert
                ? (await handler.UpdateAsync($"parents/{parent}/children/c1", request, null, advice, CancellationToken.None)).Detail
                : (await handler.CreateAsync(request, null, advice, CancellationToken.None)).Detail;
            Assert.Equal(parent, result!.ParentId);
            owner.Verify(o => o.CreateAsync(It.Is<Child>(c => c.ParentId == parent), unit.Object, It.IsAny<CancellationToken>()), Times.Once);
        } else {
            await Assert.ThrowsAsync<PermissionDeniedException>(async () => {
                if (upsert) await handler.UpdateAsync($"parents/{parent}/children/c1", request, null, advice, CancellationToken.None);
                else await handler.CreateAsync(request, null, advice, CancellationToken.None);
            });
            owner.Verify(o => o.CreateAsync(It.IsAny<Child>(), It.IsAny<IUnitOfWork?>(), It.IsAny<CancellationToken>()), Times.Never);
        }
        Assert.Equal(parent, observed);
    }

    [CanonicalName("parents/{parent_id}/children/{child}")]
    public sealed class Child : ICanonicalName, IChild, IAllowMissing {
        public string? Name { get; set; }
        public string? CanonicalName { get; set; }
        public string? Parent { get; set; }
        public string? ParentId { get; set; }
        public bool AllowMissing { get; set; } = true;
    }

    [Fact]
    public async Task Create_Persists_When_Access_Accepts_The_Mapped_Entity() {
        var request   = new Student { Name = "s1", FullName = "Created" };
        var entity    = new Student { Name = request.Name, CanonicalName = $"students/{request.Name}", FullName = request.FullName };
        var detail    = new Student { Name = entity.Name, CanonicalName = entity.CanonicalName, FullName = entity.FullName };
        var principal = new ClaimsPrincipal(new ClaimsIdentity("test"));

        var unit       = UnitOfWorkMock.Create();
        var repository = new Mock<IRepository<Student>>(MockBehavior.Strict);
        repository.Setup(r => r.Begin()).Returns(unit.Object);
        var owner = ResourceMutationMock.Create<Student>();

        var mapper = new Mock<ISimpleMapper>(MockBehavior.Strict);
        mapper.Setup(m => m.Map<Student, Student>(request)).Returns(entity);
        mapper.Setup(m => m.Map<Student, Student>(entity)).Returns(detail);

        using var services = Services(request, principal, owner, out var access);
        var advice  = new AdviceContext(services);
        var handler = new ResourceOperationHandler<Student, Student, Student, Student>(
            services, repository.Object, mapper.Object);

        var result = await handler.CreateAsync(request, principal, advice, CancellationToken.None);

        Assert.Same(detail, result.Detail);
        owner.Verify(o => o.CreateAsync(entity, unit.Object, CancellationToken.None), Times.Once);
        access.Verify(provider => provider.HasAccessAsync(entity,
                          It.Is<AccessContext<Student>>(context => context.Operation == nameof(Operations.Create) && ReferenceEquals(context.Request, request) && context.Stage == AccessStage.Instance && context.Name == entity.CanonicalName),
                          principal, It.IsAny<CancellationToken>()), Times.Once);
        access.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Create_Denial_Prevents_Add_And_Commit() {
        var request   = new Student { Name = "s1", FullName = "Blocked" };
        var entity    = new Student { Name = request.Name, CanonicalName = $"students/{request.Name}", FullName = request.FullName };
        var principal = new ClaimsPrincipal(new ClaimsIdentity("test"));

        var repository = new Mock<IRepository<Student>>(MockBehavior.Strict);
        var owner = ResourceMutationMock.Create<Student>();

        var mapper = new Mock<ISimpleMapper>(MockBehavior.Strict);
        mapper.Setup(m => m.Map<Student, Student>(request)).Returns(entity);

        using var services = Services(request, principal, owner, out var access);
        var advice  = new AdviceContext(services);
        var handler = new ResourceOperationHandler<Student, Student, Student, Student>(
            services, repository.Object, mapper.Object);

        await Assert.ThrowsAsync<PermissionDeniedException>(
            () => handler.CreateAsync(request, principal, advice, CancellationToken.None));

        owner.Verify(o => o.CreateAsync(It.IsAny<Student>(), It.IsAny<IUnitOfWork?>(), It.IsAny<CancellationToken>()), Times.Never);
        mapper.Verify(m => m.Map<Student, Student>(entity), Times.Never);
        access.Verify(provider => provider.HasAccessAsync(entity,
                          It.Is<AccessContext<Student>>(context => context.Operation == nameof(Operations.Create) && ReferenceEquals(context.Request, request) && context.Stage == AccessStage.Instance && context.Name == entity.CanonicalName),
                          principal, It.IsAny<CancellationToken>()), Times.Once);
        access.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Create_Denial_Uses_The_Registered_Default_Permission_Resolver() {
        var request = new OrderItem { Name = "i1" };
        var entity  = new OrderItem { Name = request.Name, CanonicalName = "order-items/i1" };

        var repository = new Mock<IRepository<OrderItem>>(MockBehavior.Strict);
        var mapper = new Mock<ISimpleMapper>(MockBehavior.Strict);
        mapper.Setup(m => m.Map<OrderItem, OrderItem>(request)).Returns(entity);
        var access = new Mock<IAccessProvider<OrderItem, OrderItem>>(MockBehavior.Strict);
        access.Setup(p => p.HasAccessAsync(entity, It.IsAny<AccessContext<OrderItem>>(), null, It.IsAny<CancellationToken>()))
              .ReturnsAsync(AccessDecision.Denied);
        using var services = new ServiceCollection()
            .AddSingleton<IPermissionResolver, DefaultPermissionResolver>()
            .AddSingleton(access.Object)
            .AddSingleton<IResourceCreateAdvisor<OrderItem, OrderItem, OrderItem>, ResourceCreateAccessAdvisor<OrderItem, OrderItem, OrderItem>>()
            .BuildServiceProvider();
        var advice  = new AdviceContext(services);
        var handler = new ResourceOperationHandler<OrderItem, OrderItem, OrderItem, OrderItem>(
            services, repository.Object, mapper.Object);

        var denied = await Assert.ThrowsAsync<PermissionDeniedException>(
            () => handler.CreateAsync(request, null, advice, CancellationToken.None));

        var info = Assert.Single(Assert.IsType<List<IErrorDetail>>(denied.Details).OfType<ResourceInfoDetail>());
        Assert.Contains("order-item.create", Assert.IsType<string>(info.Description), StringComparison.Ordinal);
        access.VerifyAll();
    }

    [CanonicalName("order-items/{order_item}")]
    public sealed class OrderItem : ICanonicalName
    {
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }

    private static ServiceProvider Services(
        Student                                            request,
        ClaimsPrincipal                                    principal,
        Mock<IResourceMutation<Student>>                   owner,
        out Mock<IAccessProvider<Student, Student>>        access
    ) {
        var provider = new Mock<IAccessProvider<Student, Student>>(MockBehavior.Strict);
        provider.Setup(p => p.HasAccessAsync(It.IsAny<Student>(), It.IsAny<AccessContext<Student>>(), principal, It.IsAny<CancellationToken>()))
                .Returns((Student mapped, AccessContext<Student> _, ClaimsPrincipal? _, CancellationToken _) =>
                     Task.FromResult(mapped.FullName == request.FullName && mapped.FullName == "Created" ? AccessDecision.Allowed : AccessDecision.Denied));

        var services = new ServiceCollection();
        services.AddSingleton(provider.Object);
        services.AddSingleton(owner.Object);
        services.AddSingleton<IPermissionResolver, DefaultPermissionResolver>();
        services.AddSingleton<IResourceCreateAdvisor<Student, Student, Student>, ResourceCreateAccessAdvisor<Student, Student, Student>>();

        access = provider;
        return services.BuildServiceProvider();
    }
}
