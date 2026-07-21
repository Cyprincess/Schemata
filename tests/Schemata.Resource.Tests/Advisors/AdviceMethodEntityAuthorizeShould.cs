using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
using Schemata.Resource.Foundation.Advisors;
using Schemata.Resource.Tests.Fixtures;
using Schemata.Security.Skeleton;
using Xunit;

namespace Schemata.Resource.Tests.Advisors;

public class AdviceMethodEntityAuthorizeShould
{
    [Fact]
    public async Task AnonymousGranted_DoesNotCheckAccess() {
        var access = new Mock<IAccessProvider<Student, Student>>(MockBehavior.Strict);
        var advisor = new AdviceMethodEntityAuthorize<Student, Student, Student>(access.Object);
        var ctx     = Context("archive");
        ctx.Set(new AnonymousGranted());

        var result = await advisor.AdviseAsync(ctx, new(), Entity(), null);

        Assert.Equal(AdviseResult.Continue, result);
    }

    [Fact]
    public async Task PrimaryGranted_ContinuesWithLoadedEntity() {
        var entity = Entity();
        var access = new Mock<IAccessProvider<Student, Student>>();
        access.Setup(a => a.HasAccessAsync(
                         entity,
                         It.Is<AccessContext<Student>>(c => c.Operation == "archive"),
                         It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(true);
        var advisor = new AdviceMethodEntityAuthorize<Student, Student, Student>(access.Object);

        var result = await advisor.AdviseAsync(Context("archive"), new(), entity, null);

        Assert.Equal(AdviseResult.Continue, result);
        access.VerifyAll();
    }

    [Fact]
    public async Task PrimaryDenied_ParentVisible_ThrowsPermissionDeniedWithMethodPermission() {
        var entity = Entity();
        var access = new Mock<IAccessProvider<Student, Student>>();
        access.Setup(a => a.HasAccessAsync(
                         entity,
                         It.Is<AccessContext<Student>>(c => c.Operation == "archive"),
                         It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(false);
        access.Setup(a => a.HasAccessAsync(
                         entity,
                         It.Is<AccessContext<Student>>(c => c.Operation == "Get"),
                         It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(true);
        var advisor = new AdviceMethodEntityAuthorize<Student, Student, Student>(access.Object);

        var exception = await Assert.ThrowsAsync<PermissionDeniedException>(() => advisor.AdviseAsync(
            Context("archive"), new(), entity, null));

        var resource = Assert.Single(exception.Details!.OfType<ResourceInfoDetail>());
        Assert.Equal("Permission 'student.archive' denied on resource 'students/42' (or it might not exist).",
                     resource.Description);
        access.VerifyAll();
    }

    [Fact]
    public async Task PrimaryDenied_ParentHidden_ThrowsNotFound() {
        var entity = Entity();
        var access = new Mock<IAccessProvider<Student, Student>>();
        access.Setup(a => a.HasAccessAsync(
                         entity,
                         It.IsAny<AccessContext<Student>>(),
                         It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(false);
        var advisor = new AdviceMethodEntityAuthorize<Student, Student, Student>(access.Object);

        await Assert.ThrowsAsync<NotFoundException>(() => advisor.AdviseAsync(Context("archive"), new(), entity, null));
        access.Verify(a => a.HasAccessAsync(entity,
                      It.Is<AccessContext<Student>>(c => c.Operation == "archive"),
                      It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()), Times.Once);
        access.Verify(a => a.HasAccessAsync(entity,
                      It.Is<AccessContext<Student>>(c => c.Operation == "Get"),
                      It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static AdviceContext Context(string verb) {
        var context = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        context.Set(new ResourceMethodVerb(verb));
        return context;
    }

    private static Student Entity() {
        return new() { Name = "42", CanonicalName = "students/42" };
    }
}
