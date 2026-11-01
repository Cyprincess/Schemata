using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Moq;
using Schemata.Security.Foundation;
using Schemata.Security.Skeleton;
using Schemata.Security.Tests.Fixtures;
using Xunit;

namespace Schemata.Security.Tests;

public class DefaultAccessProviderShould
{
    private readonly Mock<IPermissionMatcher>  _matcher  = new();
    private readonly Mock<IPermissionResolver> _resolver = new();

    private DefaultAccessProvider<Product, object> CreateProvider() { return new(_resolver.Object, _matcher.Object); }

    private static ClaimsPrincipal CreatePrincipal() { return new(new ClaimsIdentity("Test")); }

    [Fact]
    public async Task HasAccess_NoPrincipal_ReturnsDenied() {
        var provider = CreateProvider();
        var context  = new AccessContext<object> { Operation = "Create" };

        var result = await provider.HasAccessAsync(null, context, null);

        Assert.Equal(AccessDecision.Denied, result);
        _matcher.Verify(m => m.IsMatch(It.IsAny<ClaimsPrincipal>(), It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task HasAccess_NullOrEmptyOperation_ReturnsIndeterminate(string? operation) {
        var provider  = CreateProvider();
        var principal = CreatePrincipal();
        var context   = new AccessContext<object> { Operation = operation };

        var result = await provider.HasAccessAsync(null, context, principal);

        Assert.Equal(AccessDecision.Indeterminate, result);
        _resolver.Verify(r => r.Resolve(It.IsAny<string>(), It.IsAny<Type>()), Times.Never);
        _matcher.Verify(m => m.IsMatch(It.IsAny<ClaimsPrincipal>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task HasAccess_MatchingPermission_ReturnsAllowed() {
        _resolver.Setup(r => r.Resolve("Create", typeof(Product))).Returns("product.create");
        _matcher.Setup(m => m.IsMatch(It.IsAny<ClaimsPrincipal>(), "product.create")).Returns(true);

        var provider  = CreateProvider();
        var principal = CreatePrincipal();
        var context   = new AccessContext<object> { Operation = "Create" };

        var result = await provider.HasAccessAsync(null, context, principal);

        Assert.Equal(AccessDecision.Allowed, result);
        _resolver.Verify(r => r.Resolve("Create", typeof(Product)), Times.Once);
        _matcher.Verify(m => m.IsMatch(principal, "product.create"), Times.Once);
    }

    [Fact]
    public async Task HasAccess_NoMatchingPermission_ReturnsDenied() {
        _resolver.Setup(r => r.Resolve("Delete", typeof(Product))).Returns("product.delete");
        _matcher.Setup(m => m.IsMatch(It.IsAny<ClaimsPrincipal>(), "product.delete")).Returns(false);

        var provider  = CreateProvider();
        var principal = CreatePrincipal();
        var context   = new AccessContext<object> { Operation = "Delete" };

        var result = await provider.HasAccessAsync(null, context, principal);

        Assert.Equal(AccessDecision.Denied, result);
        _resolver.Verify(r => r.Resolve("Delete", typeof(Product)), Times.Once);
        _matcher.Verify(m => m.IsMatch(principal, "product.delete"), Times.Once);
    }

    [Fact]
    public async Task HasAccess_MissingStage_MatchingPermission_ReturnsAllowed() {
        // A matched operation permission proves the caller may learn the target is absent; the
        // default provider never fabricates a parent read-children check from it.
        _resolver.Setup(r => r.Resolve("Get", typeof(Product))).Returns("product.get");
        _matcher.Setup(m => m.IsMatch(It.IsAny<ClaimsPrincipal>(), "product.get")).Returns(true);

        var provider  = CreateProvider();
        var principal = CreatePrincipal();
        var context = new AccessContext<object> {
            Operation = "Get", Stage = AccessStage.Missing, Name = "products/p1",
        };

        var result = await provider.HasAccessAsync(null, context, principal);

        Assert.Equal(AccessDecision.Allowed, result);
    }

    [Fact]
    public async Task HasAccess_MissingStage_NoPermission_ReturnsDenied() {
        _resolver.Setup(r => r.Resolve("Get", typeof(Product))).Returns("product.get");
        _matcher.Setup(m => m.IsMatch(It.IsAny<ClaimsPrincipal>(), "product.get")).Returns(false);

        var provider  = CreateProvider();
        var principal = CreatePrincipal();
        var context = new AccessContext<object> {
            Operation = "Get", Stage = AccessStage.Missing, Name = "products/p1",
        };

        var result = await provider.HasAccessAsync(null, context, principal);

        Assert.Equal(AccessDecision.Denied, result);
    }
}
