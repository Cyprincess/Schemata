using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Resource;
using Schemata.Resource.Foundation.Advisors;
using Schemata.Resource.Foundation.Tests.Fixtures;
using Schemata.Security.Skeleton;
using Xunit;

namespace Schemata.Resource.Foundation.Tests.Advisors;

[Anonymous(Operations.Create)]
public class AnonStudent : Student
{ }

public class AdviceAuthorizeShould
{
    [Fact]
    public async Task Create_AuthorizedUser_ReturnsContinue() {
        var access = new Mock<IAccessProvider<Student, ResourceRequestContext<Student>>>();
        access.Setup(a => a.HasAccessAsync(
                         It.IsAny<Student?>(),
                         It.IsAny<ResourceRequestContext<Student>?>(),
                         It.IsAny<ClaimsPrincipal?>(),
                         It.IsAny<CancellationToken>()
                         )).ReturnsAsync(true);

        var advisor = new AdviceCreateRequestAuthorize<Student, Student>(access.Object);
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var request = new Student { FullName = "Authorized" };

        var result = await advisor.AdviseAsync(ctx, request, null);

        Assert.Equal(AdviseResult.Continue, result);
    }

    [Fact]
    public async Task Create_UnauthorizedUser_ThrowsAuthorizationException() {
        var access = new Mock<IAccessProvider<Student, ResourceRequestContext<Student>>>();
        access.Setup(a => a.HasAccessAsync(
                         It.IsAny<Student?>(),
                         It.IsAny<ResourceRequestContext<Student>?>(),
                         It.IsAny<ClaimsPrincipal?>(),
                         It.IsAny<CancellationToken>()
                         )).ReturnsAsync(false);

        var advisor = new AdviceCreateRequestAuthorize<Student, Student>(access.Object);
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var request = new Student { FullName = "Unauthorized" };

        await Assert.ThrowsAsync<AuthorizationException>(() => advisor.AdviseAsync(ctx, request, null));
    }

    [Fact]
    public async Task Create_AnonymousEntity_ContinuesWithoutCallingAccessProvider() {
        var access  = new Mock<IAccessProvider<AnonStudent, ResourceRequestContext<Student>>>();
        var advisor = new AdviceCreateRequestAuthorize<AnonStudent, Student>(access.Object);
        var ctx     = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var request = new Student { FullName = "Public" };

        var result = await advisor.AdviseAsync(ctx, request, null);

        Assert.Equal(AdviseResult.Continue, result);
        access.Verify(a => a.HasAccessAsync(
                          It.IsAny<AnonStudent?>(),
                          It.IsAny<ResourceRequestContext<Student>?>(),
                          It.IsAny<ClaimsPrincipal?>(),
                          It.IsAny<CancellationToken>()
                          ), Times.Never);
    }
}
