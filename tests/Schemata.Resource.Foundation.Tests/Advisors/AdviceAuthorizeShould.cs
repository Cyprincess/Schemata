using System;
using System.Linq;
using System.Linq.Expressions;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Resource.Foundation.Advisors;
using Schemata.Resource.Foundation.Tests.Fixtures;
using Schemata.Security.Skeleton;
using Xunit;

namespace Schemata.Resource.Foundation.Tests.Advisors;

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
    public async Task Create_WithoutAnonymousGranted_ChecksAccess() {
        var access = new Mock<IAccessProvider<Student, Student>>();
        access.Setup(a => a.HasAccessAsync(It.IsAny<Student?>(), It.IsAny<AccessContext<Student>>(), It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(false);

        var advisor   = new AdviceCreateRequestAuthorize<Student, Student>(access.Object);
        var ctx       = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var request   = new Student { FullName = "Unauthorized" };
        var container = new ResourceRequestContainer<Student>();

        await Assert.ThrowsAsync<AuthorizationException>(() => advisor.AdviseAsync(ctx, request, container, null));

        access.Verify(a => a.HasAccessAsync(It.IsAny<Student?>(), It.IsAny<AccessContext<Student>>(), It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Get_AuthorizedUser_AppliesEntitlementToContainer() {
        var access = new Mock<IAccessProvider<Student, GetRequest>>();
        access.Setup(a => a.HasAccessAsync(It.IsAny<Student?>(), It.IsAny<AccessContext<GetRequest>>(), It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(true);

        Expression<Func<Student, bool>> filter      = s => s.FullName != null;
        var                             entitlement = new Mock<IEntitlementProvider<Student, GetRequest>>();

        entitlement.Setup(e => e.GenerateEntitlementExpressionAsync(It.IsAny<AccessContext<GetRequest>>(), It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
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
        access.Setup(a => a.HasAccessAsync(It.IsAny<Student?>(), It.IsAny<AccessContext<GetRequest>>(), It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(true);

        var entitlement = new Mock<IEntitlementProvider<Student, GetRequest>>();
        entitlement.Setup(e => e.GenerateEntitlementExpressionAsync(It.IsAny<AccessContext<GetRequest>>(), It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
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
    public async Task Get_UnauthorizedUser_ThrowsAuthorizationException() {
        var access = new Mock<IAccessProvider<Student, GetRequest>>();
        access.Setup(a => a.HasAccessAsync(It.IsAny<Student?>(), It.IsAny<AccessContext<GetRequest>>(), It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(false);

        var entitlement = new Mock<IEntitlementProvider<Student, GetRequest>>();

        var advisor   = new AdviceGetRequestAuthorize<Student>(access.Object, entitlement.Object);
        var ctx       = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var container = new ResourceRequestContainer<Student>();
        var request   = new GetRequest { Name = "students/1" };

        await Assert.ThrowsAsync<AuthorizationException>(() => advisor.AdviseAsync(ctx, request, container, null));
    }
}
