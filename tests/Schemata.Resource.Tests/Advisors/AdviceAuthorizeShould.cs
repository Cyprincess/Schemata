using System;
using System.Linq;
using System.Linq.Expressions;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Resource.Foundation;
using Schemata.Resource.Foundation.Advisors;
using Schemata.Resource.Tests.Fixtures;
using Schemata.Security.Skeleton;
using Xunit;

namespace Schemata.Resource.Tests.Advisors;

public class AdviceAuthorizeShould
{
    [Fact]
    public async Task Create_WithAnonymousGranted_SkipsAccessCheck() {
        var access  = new Mock<IAccessProvider<Student, Student>>(MockBehavior.Strict);
        var advisor = new AdviceCreateRequestAuthorize<Student, Student>(access.Object);
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        ctx.Set(new AnonymousGranted());
        var request   = new Student { FullName = "Anonymous" };
        var container = new ResourceRequestContainer<Student>();

        var result = await advisor.AdviseAsync(ctx, request, container, null);

        Assert.Equal(AdviseResult.Continue, result);
    }

    [Fact]
    public async Task Create_WhenPrimaryAndParentDenied_ThrowsNotFound() {
        var access = new Mock<IAccessProvider<Student, Student>>();
        access.Setup(a => a.HasAccessAsync(
                         It.IsAny<Student?>(),
                         It.IsAny<AccessContext<Student>>(),
                         It.IsAny<ClaimsPrincipal?>(),
                         It.IsAny<CancellationToken>()
                     )
               )
              .ReturnsAsync(false);

        var advisor   = new AdviceCreateRequestAuthorize<Student, Student>(access.Object);
        var ctx       = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var request   = new Student { FullName = "Unauthorized", Name = "students/x" };
        var container = new ResourceRequestContainer<Student>();

        await Assert.ThrowsAsync<NotFoundException>(() => advisor.AdviseAsync(ctx, request, container, null));
    }

    [Fact]
    public async Task Create_WhenPrimaryDeniedButParentGranted_ThrowsAuthorizationWithTemplate() {
        var access = new Mock<IAccessProvider<Student, Student>>();
        access.Setup(a => a.HasAccessAsync(
                         It.IsAny<Student?>(),
                         It.Is<AccessContext<Student>>(c => c.Operation == nameof(Operations.Create)),
                         It.IsAny<ClaimsPrincipal?>(),
                         It.IsAny<CancellationToken>()
                     )
               )
              .ReturnsAsync(false);
        access.Setup(a => a.HasAccessAsync(
                         It.IsAny<Student?>(),
                         It.Is<AccessContext<Student>>(c => c.Operation == nameof(Operations.Get)),
                         It.IsAny<ClaimsPrincipal?>(),
                         It.IsAny<CancellationToken>()
                     )
               )
              .ReturnsAsync(true);

        var advisor   = new AdviceCreateRequestAuthorize<Student, Student>(access.Object);
        var ctx       = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var request   = new Student { FullName = "Visible", Name = "students/42" };
        var container = new ResourceRequestContainer<Student>();

        var ex = await Assert.ThrowsAsync<AuthorizationException>(() => advisor.AdviseAsync(
                                                                      ctx,
                                                                      request,
                                                                      container,
                                                                      null
                                                                  )
        );
        Assert.Equal(
            "Permission 'Student.Create' denied on resource 'students/42' (or it might not exist).",
            ex.Message
        );
    }

    [Fact]
    public async Task Create_WhenPrimaryGranted_Continues() {
        var access = new Mock<IAccessProvider<Student, Student>>();
        access.Setup(a => a.HasAccessAsync(
                         It.IsAny<Student?>(),
                         It.IsAny<AccessContext<Student>>(),
                         It.IsAny<ClaimsPrincipal?>(),
                         It.IsAny<CancellationToken>()
                     )
               )
              .ReturnsAsync(true);

        var advisor   = new AdviceCreateRequestAuthorize<Student, Student>(access.Object);
        var ctx       = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var request   = new Student { FullName = "Authorized" };
        var container = new ResourceRequestContainer<Student>();

        var result = await advisor.AdviseAsync(ctx, request, container, null);

        Assert.Equal(AdviseResult.Continue, result);
        access.Verify(
            a => a.HasAccessAsync(
                It.IsAny<Student?>(),
                It.Is<AccessContext<Student>>(c => c.Operation == nameof(Operations.Create)),
                It.IsAny<ClaimsPrincipal?>(),
                It.IsAny<CancellationToken>()
            ),
            Times.Once
        );
        access.Verify(
            a => a.HasAccessAsync(
                It.IsAny<Student?>(),
                It.Is<AccessContext<Student>>(c => c.Operation == nameof(Operations.Get)),
                It.IsAny<ClaimsPrincipal?>(),
                It.IsAny<CancellationToken>()
            ),
            Times.Never
        );
    }

    [Fact]
    public async Task Get_AuthorizedUser_AppliesEntitlementToContainer() {
        var access = new Mock<IAccessProvider<Student, GetRequest>>();
        access.Setup(a => a.HasAccessAsync(
                         It.IsAny<Student?>(),
                         It.IsAny<AccessContext<GetRequest>>(),
                         It.IsAny<ClaimsPrincipal?>(),
                         It.IsAny<CancellationToken>()
                     )
               )
              .ReturnsAsync(true);

        Expression<Func<Student, bool>> filter      = s => s.FullName != null;
        var                             entitlement = new Mock<IEntitlementProvider<Student, GetRequest>>();

        entitlement.Setup(e => e.GenerateEntitlementExpressionAsync(
                              It.IsAny<AccessContext<GetRequest>>(),
                              It.IsAny<ClaimsPrincipal?>(),
                              It.IsAny<CancellationToken>()
                          )
                    )
                   .ReturnsAsync(filter);

        var advisor   = new AdviceGetRequestAuthorize<Student>(access.Object, entitlement.Object);
        var ctx       = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var container = new ResourceRequestContainer<Student>();
        var request   = new GetRequest { Name = "students/1" };

        var result = await advisor.AdviseAsync(ctx, request, container, null);

        Assert.Equal(AdviseResult.Continue, result);

        var students = new[] { new Student { FullName = "Alice" }, new Student { FullName = null } };
        var filtered = container.Query(students.AsQueryable()).ToList();
        Assert.Single(filtered);
        Assert.Equal("Alice", filtered[0].FullName);
    }

    [Fact]
    public async Task Get_AuthorizedUser_NullEntitlement_LeavesContainerUnmodified() {
        var access = new Mock<IAccessProvider<Student, GetRequest>>();
        access.Setup(a => a.HasAccessAsync(
                         It.IsAny<Student?>(),
                         It.IsAny<AccessContext<GetRequest>>(),
                         It.IsAny<ClaimsPrincipal?>(),
                         It.IsAny<CancellationToken>()
                     )
               )
              .ReturnsAsync(true);

        var entitlement = new Mock<IEntitlementProvider<Student, GetRequest>>();
        entitlement.Setup(e => e.GenerateEntitlementExpressionAsync(
                              It.IsAny<AccessContext<GetRequest>>(),
                              It.IsAny<ClaimsPrincipal?>(),
                              It.IsAny<CancellationToken>()
                          )
                    )
                   .ReturnsAsync((Expression<Func<Student, bool>>?)null);

        var advisor   = new AdviceGetRequestAuthorize<Student>(access.Object, entitlement.Object);
        var ctx       = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var container = new ResourceRequestContainer<Student>();
        var request   = new GetRequest { Name = "students/1" };

        var result = await advisor.AdviseAsync(ctx, request, container, null);

        Assert.Equal(AdviseResult.Continue, result);

        var students = new[] { new Student { FullName = "Alice" } };
        var filtered = container.Query(students.AsQueryable()).ToList();
        Assert.Single(filtered);
    }

    [Fact]
    public async Task Get_UnauthorizedAndParentHidden_ThrowsNotFound() {
        var access = new Mock<IAccessProvider<Student, GetRequest>>();
        access.Setup(a => a.HasAccessAsync(
                         It.IsAny<Student?>(),
                         It.IsAny<AccessContext<GetRequest>>(),
                         It.IsAny<ClaimsPrincipal?>(),
                         It.IsAny<CancellationToken>()
                     )
               )
              .ReturnsAsync(false);

        var entitlement = new Mock<IEntitlementProvider<Student, GetRequest>>();

        var advisor   = new AdviceGetRequestAuthorize<Student>(access.Object, entitlement.Object);
        var ctx       = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var container = new ResourceRequestContainer<Student>();
        var request   = new GetRequest { Name = "students/1" };

        await Assert.ThrowsAsync<NotFoundException>(() => advisor.AdviseAsync(ctx, request, container, null));
    }

    [Fact]
    public async Task Get_UnauthorizedButParentVisible_DisclosesPermission() {
        // Parent re-probe on GetRequest reuses the same Operation=Get context, so for Get the primary and
        // parent checks are identical. To simulate "primary denied, parent visible" the provider must
        // flip its answer across invocations. Sequential setup captures that ordering.
        var access = new Mock<IAccessProvider<Student, GetRequest>>();
        access.SetupSequence(a => a.HasAccessAsync(
                                 It.IsAny<Student?>(),
                                 It.IsAny<AccessContext<GetRequest>>(),
                                 It.IsAny<ClaimsPrincipal?>(),
                                 It.IsAny<CancellationToken>()
                             )
               )
              .ReturnsAsync(false)
              .ReturnsAsync(true);

        var entitlement = new Mock<IEntitlementProvider<Student, GetRequest>>();

        var advisor   = new AdviceGetRequestAuthorize<Student>(access.Object, entitlement.Object);
        var ctx       = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var container = new ResourceRequestContainer<Student>();
        var request   = new GetRequest { Name = "students/7" };

        await Assert.ThrowsAsync<NotFoundException>(() => advisor.AdviseAsync(ctx, request, container, null));
    }

    [Fact]
    public async Task List_UnauthorizedAndParentHidden_ThrowsNotFound() {
        var access = new Mock<IAccessProvider<Student, ListRequest>>();
        access.Setup(a => a.HasAccessAsync(
                         It.IsAny<Student?>(),
                         It.IsAny<AccessContext<ListRequest>>(),
                         It.IsAny<ClaimsPrincipal?>(),
                         It.IsAny<CancellationToken>()
                     )
               )
              .ReturnsAsync(false);

        var entitlement = new Mock<IEntitlementProvider<Student, ListRequest>>();

        var advisor   = new AdviceListRequestAuthorize<Student>(access.Object, entitlement.Object);
        var ctx       = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var container = new ResourceRequestContainer<Student>();
        var request   = new ListRequest { Parent = "schools/1" };

        await Assert.ThrowsAsync<NotFoundException>(() => advisor.AdviseAsync(ctx, request, container, null));
    }

    [Fact]
    public async Task List_UnauthorizedButParentVisible_DisclosesPermissionOnParent() {
        var access = new Mock<IAccessProvider<Student, ListRequest>>();
        access.Setup(a => a.HasAccessAsync(
                         It.IsAny<Student?>(),
                         It.Is<AccessContext<ListRequest>>(c => c.Operation == nameof(Operations.List)),
                         It.IsAny<ClaimsPrincipal?>(),
                         It.IsAny<CancellationToken>()
                     )
               )
              .ReturnsAsync(false);
        access.Setup(a => a.HasAccessAsync(
                         It.IsAny<Student?>(),
                         It.Is<AccessContext<ListRequest>>(c => c.Operation == nameof(Operations.Get)),
                         It.IsAny<ClaimsPrincipal?>(),
                         It.IsAny<CancellationToken>()
                     )
               )
              .ReturnsAsync(true);

        var entitlement = new Mock<IEntitlementProvider<Student, ListRequest>>();

        var advisor   = new AdviceListRequestAuthorize<Student>(access.Object, entitlement.Object);
        var ctx       = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var container = new ResourceRequestContainer<Student>();
        var request   = new ListRequest { Parent = "schools/9" };

        var ex = await Assert.ThrowsAsync<AuthorizationException>(() => advisor.AdviseAsync(
                                                                      ctx,
                                                                      request,
                                                                      container,
                                                                      null
                                                                  )
        );
        Assert.Equal("Permission 'Student.List' denied on resource 'schools/9' (or it might not exist).", ex.Message);
    }

    [Fact]
    public async Task Delete_UnauthorizedAndParentHidden_ThrowsNotFound() {
        var access = new Mock<IAccessProvider<Student, DeleteRequest>>();
        access.Setup(a => a.HasAccessAsync(
                         It.IsAny<Student?>(),
                         It.IsAny<AccessContext<DeleteRequest>>(),
                         It.IsAny<ClaimsPrincipal?>(),
                         It.IsAny<CancellationToken>()
                     )
               )
              .ReturnsAsync(false);

        var entitlement = new Mock<IEntitlementProvider<Student, DeleteRequest>>();

        var advisor   = new AdviceDeleteRequestAuthorize<Student>(access.Object, entitlement.Object);
        var ctx       = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var container = new ResourceRequestContainer<Student>();
        var request   = new DeleteRequest { Name = "students/7" };

        await Assert.ThrowsAsync<NotFoundException>(() => advisor.AdviseAsync(ctx, request, container, null));
    }
}
