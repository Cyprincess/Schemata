using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.Repository;
using Schemata.Mapping.Skeleton;
using Schemata.Resource.Foundation;
using Schemata.Resource.Foundation.Advisors;
using Schemata.Resource.Tests.Fixtures;
using Schemata.Abstractions.Resource;
using Schemata.Security.Skeleton;
using Schemata.Abstractions.Advisors;
using Xunit;

namespace Schemata.Resource.Tests;

/// <summary>
///     Behavioral coverage for missing-target authorization finalization, per issue #33: an
///     entitlement-filtered or absent load result is disclosed as NOT_FOUND only when the
///     applicable access policy permits it; a definite denial or an undecidable policy is a real
///     PERMISSION_DENIED, and allow-missing create / empty-success continuations run only after
///     the finalization passes.
/// </summary>
public class ResourceMissingAccessShould
{
    [CanonicalName("schools/{school}/students/{student}")]
    public sealed class EnrolledStudent : ICanonicalName
    {
        public string? School        { get; set; }

        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }

    private static (Mock<IRepository<Student>> Repository, Mock<ISimpleMapper> Mapper) AbsentLoad() {
        var repository = new Mock<IRepository<Student>>();
        repository.Setup(r => r.SingleOrDefaultAsync(It.IsAny<Func<IQueryable<Student>, IQueryable<Student>>>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync((Student?)null);
        var mapper = new Mock<ISimpleMapper>(MockBehavior.Loose);
        return (repository, mapper);
    }

    private static async Task<T> InvokeAsync<T>(Func<Task<T>> action) {
        using var ambient = AdviceContext.Establish(new(new ServiceCollection().BuildServiceProvider()));
        return await action();
    }

    private static ResourceOperationHandler<Student, Student, Student, Student> Handler(
        IServiceProvider services,
        IRepository<Student>                     repository,
        ISimpleMapper                            mapper
    ) {
        return new(services, repository, mapper);
    }

    private static ServiceProvider Services<TEntity>(
        IAccessProvider<TEntity, GetRequest>?   get = null,
        IAccessProvider<TEntity, TEntity>?      update = null,
        IAccessProvider<TEntity, DeleteRequest>? delete = null
    )
        where TEntity : class {
        var services = new ServiceCollection();
        if (get is not null) {
            services.AddSingleton(get);
            services.AddKeyedScoped<ResourceAccessStage>(typeof(TEntity));
        }

        if (update is not null) {
            services.AddSingleton(update);
            services.AddKeyedScoped<ResourceAccessStage>(typeof(TEntity));
        }

        if (delete is not null) {
            services.AddSingleton(delete);
            services.AddKeyedScoped<ResourceAccessStage>(typeof(TEntity));
        }

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Get_MissingTarget_PermittedPolicy_DisclosesNotFound() {
        var (repository, mapper) = AbsentLoad();
        var access = new Mock<IAccessProvider<Student, GetRequest>>();
        access.Setup(provider => provider.HasAccessAsync(
                         It.IsAny<Student?>(),
                         It.IsAny<AccessContext<GetRequest>>(),
                         It.IsAny<ClaimsPrincipal?>(),
                         It.IsAny<CancellationToken>()))
              .ReturnsAsync(AccessDecision.Allowed);
        using var services = Services<Student>(get: access.Object);
        var handler = Handler(services, repository.Object, mapper.Object);

        var ex = await InvokeAsync(() => Assert.ThrowsAsync<NotFoundException>(
            () => handler.GetAsync(new GetRequest { Name = "students/absent" }, null, CancellationToken.None)));

        // A definite Allowed at the target stage discloses the absence directly.
        access.Verify(provider => provider.HasAccessAsync(
            null,
            It.Is<AccessContext<GetRequest>>(context => context.Stage == AccessStage.Target
                                                      && context.Operation == nameof(Operations.Get)
                                                      && context.Name == "students/absent"),
            It.IsAny<ClaimsPrincipal?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        access.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(AccessDecision.Denied)]
    [InlineData(AccessDecision.Indeterminate)]
    public async Task Get_MissingTarget_UnpermittedPolicy_ThrowsPermissionDenied(AccessDecision decision) {
        var (repository, mapper) = AbsentLoad();
        var access = new Mock<IAccessProvider<Student, GetRequest>>();
        access.Setup(provider => provider.HasAccessAsync(
                         It.IsAny<Student?>(),
                         It.IsAny<AccessContext<GetRequest>>(),
                         It.IsAny<ClaimsPrincipal?>(),
                         It.IsAny<CancellationToken>()))
              .ReturnsAsync(decision);
        using var services = Services<Student>(get: access.Object);
        var handler = Handler(services, repository.Object, mapper.Object);

        var ex = await InvokeAsync(() => Assert.ThrowsAsync<PermissionDeniedException>(
            () => handler.GetAsync(new GetRequest { Name = "students/absent" }, new(new ClaimsIdentity("test")), CancellationToken.None)));

        Assert.Equal("PERMISSION_DENIED", ex.Status);
    }

    [Fact]
    public async Task Delete_MissingTarget_DeniedPolicy_FailsBeforeAllowMissingSuccess() {
        var (repository, mapper) = AbsentLoad();
        var access = new Mock<IAccessProvider<Student, DeleteRequest>>();
        access.Setup(provider => provider.HasAccessAsync(
                         It.IsAny<Student?>(),
                         It.IsAny<AccessContext<DeleteRequest>>(),
                         It.IsAny<ClaimsPrincipal?>(),
                         It.IsAny<CancellationToken>()))
              .ReturnsAsync(AccessDecision.Denied);
        using var services = Services<Student>(delete: access.Object);
        var handler = Handler(services, repository.Object, mapper.Object);

        var ex = await InvokeAsync(() => Assert.ThrowsAsync<PermissionDeniedException>(
            () => handler.DeleteAsync("students/absent", null, new(new ClaimsIdentity("test")), CancellationToken.None, allowMissing: true)));

        Assert.Equal("PERMISSION_DENIED", ex.Status);
    }

    [Fact]
    public async Task Update_MissingTarget_DeniedPolicy_FailsBeforeCreateOnMissing() {
        var (repository, mapper) = AbsentLoad();
        var access = new Mock<IAccessProvider<Student, Student>>();
        access.Setup(provider => provider.HasAccessAsync(
                         It.IsAny<Student?>(),
                         It.IsAny<AccessContext<Student>>(),
                         It.IsAny<ClaimsPrincipal?>(),
                         It.IsAny<CancellationToken>()))
              .ReturnsAsync(AccessDecision.Denied);
        using var services = Services<Student>(update: access.Object);
        var handler = Handler(services, repository.Object, mapper.Object);

        var request = new Student {
            Name = "absent", CanonicalName = "students/absent", AllowMissing = true,
        };

        var ex = await InvokeAsync(() => Assert.ThrowsAsync<PermissionDeniedException>(
            () => handler.UpdateAsync("students/absent", request, new(new ClaimsIdentity("test")), CancellationToken.None)));

        Assert.Equal("PERMISSION_DENIED", ex.Status);
        repository.Verify(r => r.AddAsync(It.IsAny<Student>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Get_MissingTarget_IndeterminateParentAllowed_DisclosesWithRealParent() {
        // Target undecidable, parent read-children policy permits: disclose, with the actual
        // collection parent derived from the child canonical name.
        var repository = new Mock<IRepository<EnrolledStudent>>();
        repository.Setup(r => r.SingleOrDefaultAsync(It.IsAny<Func<IQueryable<EnrolledStudent>, IQueryable<EnrolledStudent>>>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync((EnrolledStudent?)null);
        var mapper = new Mock<ISimpleMapper>(MockBehavior.Loose);
        var access = new Mock<IAccessProvider<EnrolledStudent, GetRequest>>();
        access.SetupSequence(provider => provider.HasAccessAsync(
                       It.IsAny<EnrolledStudent?>(),
                       It.IsAny<AccessContext<GetRequest>>(),
                       It.IsAny<ClaimsPrincipal?>(),
                       It.IsAny<CancellationToken>()))
              .ReturnsAsync(AccessDecision.Indeterminate)
              .ReturnsAsync(AccessDecision.Allowed);
        using var services = Services<EnrolledStudent>(get: access.Object);
        var handler = new ResourceOperationHandler<EnrolledStudent, EnrolledStudent, EnrolledStudent, EnrolledStudent>(services, repository.Object, mapper.Object);

        var ex = await InvokeAsync(() => Assert.ThrowsAsync<NotFoundException>(
            () => handler.GetAsync(new GetRequest { Name = "schools/alpha/students/absent" }, null, CancellationToken.None)));

        access.Verify(provider => provider.HasAccessAsync(
            null,
            It.Is<AccessContext<GetRequest>>(context => context.Stage == AccessStage.Missing
                                                      && context.Name == "schools/alpha/students/absent"
                                                      && context.Parent == "schools/alpha"),
            It.IsAny<ClaimsPrincipal?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Get_MissingTarget_IndeterminateParentDenied_ThrowsPermissionDenied() {
        var (repository, mapper) = AbsentLoad();
        var access = new Mock<IAccessProvider<Student, GetRequest>>();
        access.SetupSequence(provider => provider.HasAccessAsync(
                       It.IsAny<Student?>(),
                       It.IsAny<AccessContext<GetRequest>>(),
                       It.IsAny<ClaimsPrincipal?>(),
                       It.IsAny<CancellationToken>()))
              .ReturnsAsync(AccessDecision.Indeterminate)
              .ReturnsAsync(AccessDecision.Indeterminate);
        using var services = Services<Student>(get: access.Object);
        var handler = Handler(services, repository.Object, mapper.Object);

        var ex = await InvokeAsync(() => Assert.ThrowsAsync<PermissionDeniedException>(
            () => handler.GetAsync(new GetRequest { Name = "students/absent" }, new(new ClaimsIdentity("test")), CancellationToken.None)));

        Assert.Equal("PERMISSION_DENIED", ex.Status);
    }

    [Fact]
    public async Task Get_MissingTarget_TargetDenied_ThrowsWithoutMissingConsultation() {
        // A definite target denial terminates immediately — the parent policy is never consulted.
        var (repository, mapper) = AbsentLoad();
        var access = new Mock<IAccessProvider<Student, GetRequest>>();
        access.Setup(provider => provider.HasAccessAsync(
                         It.IsAny<Student?>(),
                         It.Is<AccessContext<GetRequest>>(context => context.Stage == AccessStage.Target),
                         It.IsAny<ClaimsPrincipal?>(),
                         It.IsAny<CancellationToken>()))
              .ReturnsAsync(AccessDecision.Denied);
        using var services = Services<Student>(get: access.Object);
        var handler = Handler(services, repository.Object, mapper.Object);

        await InvokeAsync(() => Assert.ThrowsAsync<PermissionDeniedException>(
            () => handler.GetAsync(new GetRequest { Name = "students/absent" }, new(new ClaimsIdentity("test")), CancellationToken.None)));

        access.Verify(provider => provider.HasAccessAsync(
            It.IsAny<Student?>(),
            It.Is<AccessContext<GetRequest>>(context => context.Stage == AccessStage.Missing),
            It.IsAny<ClaimsPrincipal?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Get_MissingTarget_Without_Access_Provider_KeepsNotFound() {
        var (repository, mapper) = AbsentLoad();
        using var services = Services<Student>();
        var handler = Handler(services, repository.Object, mapper.Object);

        // No security feature registered: no provider, no finalization — prior behavior.
        await InvokeAsync(() => Assert.ThrowsAsync<NotFoundException>(
            () => handler.GetAsync(new GetRequest { Name = "students/absent" }, null, CancellationToken.None)));
    }
}
