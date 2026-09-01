using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Security.Skeleton.Advisors;
using Schemata.Security.Skeleton;
using Schemata.Security.Tests.Fixtures;
using Xunit;

namespace Schemata.Security.Tests;

public class AuthorizationPipelineAdvisorShould
{
    private readonly Mock<IPermissionMatcher>  _matcher  = new();
    private readonly Mock<IPermissionResolver> _resolver = new();

    [Fact]
    public void Expose_The_Authorization_Order() {
        var advisor = CreateAdvisor(_ => (nameof(Operations.Get), typeof(Product)));

        Assert.Equal(SecurityOrders.Authorization, advisor.Order);
    }

    [Fact]
    public async Task Continue_When_The_Principal_Matches_The_Resolved_Permission() {
        var principal = AuthenticatedPrincipal();
        var request   = new TestRequest(principal);
        _resolver.Setup(value => value.Resolve(nameof(Operations.Update), typeof(Product))).Returns("product.update");
        _matcher.Setup(value => value.IsMatch(principal, "product.update")).Returns(true);
        var advisor = CreateAdvisor(_ => (nameof(Operations.Update), typeof(Product)));
        var calls   = 0;

        Task<string> Next(CancellationToken _) {
            calls++;
            return Task.FromResult("completed");
        }

        var result = await advisor.AdviseAsync(Context(), request, Next, CancellationToken.None);

        Assert.Equal("completed", result);
        Assert.Equal(1, calls);
        _resolver.Verify(value => value.Resolve(nameof(Operations.Update), typeof(Product)), Times.Once);
        _matcher.Verify(value => value.IsMatch(principal, "product.update"), Times.Once);
    }

    [Theory]
    [InlineData(nameof(Operations.List))]
    [InlineData(nameof(Operations.Create))]
    [InlineData("Approve")]
    public async Task Reject_Non_Existence_Sensitive_Denials_With_Permission_Denied(string operation) {
        var principal = AuthenticatedPrincipal();
        _resolver.Setup(value => value.Resolve(operation, typeof(Product))).Returns("product.operation");
        _matcher.Setup(value => value.IsMatch(principal, "product.operation")).Returns(false);
        var advisor = CreateAdvisor(_ => (operation, typeof(Product)));
        var calls   = 0;

        Task<string> Next(CancellationToken _) {
            calls++;
            return Task.FromResult("completed");
        }

        var exception = await Assert.ThrowsAsync<PermissionDeniedException>(() =>
                                                                                advisor.AdviseAsync(Context(), new TestRequest(principal), Next, CancellationToken.None));

        Assert.Equal(403, exception.Code);
        Assert.Equal("PERMISSION_DENIED", exception.Status);
        Assert.Equal(0, calls);
        _resolver.Verify(value => value.Resolve(nameof(Operations.Get), typeof(Product)), Times.Never);
    }

    [Fact]
    public async Task Reveal_A_Denied_Delete_When_Get_Permission_Matches() {
        var principal = AuthenticatedPrincipal();
        _resolver.Setup(value => value.Resolve(nameof(Operations.Delete), typeof(Product))).Returns("product.delete");
        _resolver.Setup(value => value.Resolve(nameof(Operations.Get), typeof(Product))).Returns("product.get");
        _matcher.Setup(value => value.IsMatch(principal, "product.delete")).Returns(false);
        _matcher.Setup(value => value.IsMatch(principal, "product.get")).Returns(true);
        var advisor = CreateAdvisor(_ => (nameof(Operations.Delete), typeof(Product)));

        var exception = await Assert.ThrowsAsync<PermissionDeniedException>(() =>
                                                                                advisor.AdviseAsync(Context(), new TestRequest(principal), _ => Task.FromResult("completed"), CancellationToken.None));

        Assert.Equal(403, exception.Code);
        _resolver.Verify(value => value.Resolve(nameof(Operations.Get), typeof(Product)), Times.Once);
    }

    [Fact]
    public async Task Hide_A_Denied_Get_As_Not_Found() {
        var principal = AuthenticatedPrincipal();
        _resolver.Setup(value => value.Resolve(nameof(Operations.Get), typeof(Product))).Returns("product.get");
        _matcher.Setup(value => value.IsMatch(principal, "product.get")).Returns(false);
        var advisor = CreateAdvisor(_ => (nameof(Operations.Get), typeof(Product)));

        var exception = await Assert.ThrowsAsync<NotFoundException>(() =>
                                                                        advisor.AdviseAsync(Context(), new TestRequest(principal), _ => Task.FromResult("completed"), CancellationToken.None));

        Assert.Equal(404, exception.Code);
        Assert.Equal("NOT_FOUND", exception.Status);
        _resolver.Verify(value => value.Resolve(nameof(Operations.Get), typeof(Product)), Times.Once);
    }

    [Fact]
    public async Task Reveal_A_Denied_Update_When_Get_Permission_Matches() {
        var principal = AuthenticatedPrincipal();
        _resolver.Setup(value => value.Resolve(nameof(Operations.Update), typeof(Product))).Returns("product.update");
        _resolver.Setup(value => value.Resolve(nameof(Operations.Get), typeof(Product))).Returns("product.get");
        _matcher.Setup(value => value.IsMatch(principal, "product.update")).Returns(false);
        _matcher.Setup(value => value.IsMatch(principal, "product.get")).Returns(true);
        var advisor = CreateAdvisor(_ => (nameof(Operations.Update), typeof(Product)));

        var exception = await Assert.ThrowsAsync<PermissionDeniedException>(() =>
                                                                                advisor.AdviseAsync(Context(), new TestRequest(principal), _ => Task.FromResult("completed"), CancellationToken.None));

        Assert.Equal(403, exception.Code);
        _resolver.Verify(value => value.Resolve(nameof(Operations.Get), typeof(Product)), Times.Once);
        _matcher.Verify(value => value.IsMatch(principal, "product.get"), Times.Once);
    }

    [Fact]
    public async Task Hide_A_Denied_Update_When_Get_Permission_Does_Not_Match() {
        var principal = AuthenticatedPrincipal();
        _resolver.Setup(value => value.Resolve(nameof(Operations.Update), typeof(Product))).Returns("product.update");
        _resolver.Setup(value => value.Resolve(nameof(Operations.Get), typeof(Product))).Returns("product.get");
        _matcher.Setup(value => value.IsMatch(principal, "product.update")).Returns(false);
        _matcher.Setup(value => value.IsMatch(principal, "product.get")).Returns(false);
        var advisor = CreateAdvisor(_ => (nameof(Operations.Update), typeof(Product)));

        var exception = await Assert.ThrowsAsync<NotFoundException>(() =>
                                                                        advisor.AdviseAsync(Context(), new TestRequest(principal), _ => Task.FromResult("completed"), CancellationToken.None));

        Assert.Equal(404, exception.Code);
        _resolver.Verify(value => value.Resolve(nameof(Operations.Get), typeof(Product)), Times.Once);
        _matcher.Verify(value => value.IsMatch(principal, "product.get"), Times.Once);
    }

    [Fact]
    public async Task Continue_A_Matching_Permission_Once_With_Its_Cancellation_Token() {
        var principal = AuthenticatedPrincipal();
        _resolver.Setup(value => value.Resolve(nameof(Operations.List), typeof(Product))).Returns("product.list");
        _matcher.Setup(value => value.IsMatch(principal, "product.list")).Returns(true);
        var       advisor      = CreateAdvisor(_ => (nameof(Operations.List), typeof(Product)));
        using var cancellation = new CancellationTokenSource();
        var       calls        = 0;
        var       received     = default(CancellationToken);

        Task<string> Next(CancellationToken ct) {
            calls++;
            received = ct;
            return Task.FromResult("completed");
        }

        var result = await advisor.AdviseAsync(Context(), new TestRequest(principal), Next, cancellation.Token);

        Assert.Equal("completed", result);
        Assert.Equal(1, calls);
        Assert.Equal(cancellation.Token, received);
    }

    [Fact]
    public async Task Bypass_Authorization_For_An_Anonymous_Entity_Operation() {
        var advisor = CreateAdvisor(_ => (nameof(Operations.Create), typeof(PublicProduct)));

        var result = await advisor.AdviseAsync(Context(), new TestRequest(null), _ => Task.FromResult("completed"), CancellationToken.None);

        Assert.Equal("completed", result);
        _resolver.Verify(value => value.Resolve(It.IsAny<string>(), It.IsAny<Type>()), Times.Never);
        _matcher.Verify(value => value.IsMatch(It.IsAny<ClaimsPrincipal>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Bypass_Authorization_When_The_Resolver_Has_No_Entity() {
        var advisor = CreateAdvisor(_ => ("Lookup", null));

        var result = await advisor.AdviseAsync(Context(), new TestRequest(null), _ => Task.FromResult("completed"), CancellationToken.None);

        Assert.Equal("completed", result);
        _resolver.Verify(value => value.Resolve(It.IsAny<string>(), It.IsAny<Type>()), Times.Never);
    }

    [Fact]
    public async Task Reject_A_Null_Principal_Without_Calling_The_Matcher() {
        _resolver.Setup(value => value.Resolve(nameof(Operations.List), typeof(Product))).Returns("product.list");
        var advisor = CreateAdvisor(_ => (nameof(Operations.List), typeof(Product)));
        var calls   = 0;

        Task<string> Next(CancellationToken _) {
            calls++;
            return Task.FromResult("completed");
        }

        var exception = await Assert.ThrowsAsync<PermissionDeniedException>(() =>
                                                                                advisor.AdviseAsync(Context(), new TestRequest(null), Next, CancellationToken.None));

        Assert.Equal(403, exception.Code);
        Assert.Equal(0, calls);
        _matcher.Verify(value => value.IsMatch(It.IsAny<ClaimsPrincipal>(), It.IsAny<string>()), Times.Never);
    }

    private AuthorizationPipelineAdvisor<TestRequest, string> CreateAdvisor(Func<TestRequest, (string Operation, Type? Entity)> resolve) {
        return new(resolve, _resolver.Object, _matcher.Object);
    }

    private static ClaimsPrincipal AuthenticatedPrincipal() { return new(new ClaimsIdentity("test")); }

    private static AdviceContext Context() { return new(Mock.Of<IServiceProvider>()); }
}