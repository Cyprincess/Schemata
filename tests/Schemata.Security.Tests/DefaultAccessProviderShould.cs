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
    public async Task HasAccess_NoPrincipal_ReturnsFalse() {
        var provider = CreateProvider();
        var context  = new AccessContext<object> { Operation = "Create" };

        var result = await provider.HasAccessAsync(null, context, null);

        Assert.False(result);
        _matcher.Verify(m => m.IsMatch(It.IsAny<ClaimsPrincipal>(), It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task HasAccess_NullOrEmptyOperation_ReturnsFalse(string? operation) {
        var provider  = CreateProvider();
        var principal = CreatePrincipal();
        var context   = new AccessContext<object> { Operation = operation };

        var result = await provider.HasAccessAsync(null, context, principal);

        Assert.False(result);
        _resolver.Verify(r => r.Resolve(It.IsAny<string>(), It.IsAny<Type>()), Times.Never);
        _matcher.Verify(m => m.IsMatch(It.IsAny<ClaimsPrincipal>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task HasAccess_MatchingPermission_ReturnsTrue() {
        _resolver.Setup(r => r.Resolve("Create", typeof(Product))).Returns("product.create");
        _matcher.Setup(m => m.IsMatch(It.IsAny<ClaimsPrincipal>(), "product.create")).Returns(true);

        var provider  = CreateProvider();
        var principal = CreatePrincipal();
        var context   = new AccessContext<object> { Operation = "Create" };

        var result = await provider.HasAccessAsync(null, context, principal);

        Assert.True(result);
        _resolver.Verify(r => r.Resolve("Create", typeof(Product)), Times.Once);
        _matcher.Verify(m => m.IsMatch(principal, "product.create"), Times.Once);
    }

    [Fact]
    public async Task HasAccess_NoMatchingPermission_ReturnsFalse() {
        _resolver.Setup(r => r.Resolve("Delete", typeof(Product))).Returns("product.delete");
        _matcher.Setup(m => m.IsMatch(It.IsAny<ClaimsPrincipal>(), "product.delete")).Returns(false);

        var provider  = CreateProvider();
        var principal = CreatePrincipal();
        var context   = new AccessContext<object> { Operation = "Delete" };

        var result = await provider.HasAccessAsync(null, context, principal);

        Assert.False(result);
        _resolver.Verify(r => r.Resolve("Delete", typeof(Product)), Times.Once);
        _matcher.Verify(m => m.IsMatch(principal, "product.delete"), Times.Once);
    }
}
