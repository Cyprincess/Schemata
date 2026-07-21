using System.Threading.Tasks;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Moq;
using Schemata.Abstractions.Exceptions;
using Schemata.Resource.Foundation.Models;
using Xunit;

namespace Schemata.Resource.Tests.Models;

public class PageTokenShould
{
    [Fact]
    public async Task RoundTrip_PreservesAllFields() {
        var original = new PageToken {
            Filter      = "age > 18",
            OrderBy     = "name DESC",
            PageSize    = 25,
            Skip        = 50,
            ShowDeleted = false,
        };

        var protector = CreateRoundTripProtector();
        var encoded = await original.ToStringAsync(protector);
        Assert.NotNull(encoded);
        Assert.NotEmpty(encoded);

        var decoded = await PageToken.FromStringAsync(encoded, protector);
        Assert.NotNull(decoded);
        Assert.Equal(original.Filter, decoded.Filter);
        Assert.Equal(original.OrderBy, decoded.OrderBy);
        Assert.Equal(original.PageSize, decoded.PageSize);
        Assert.Equal(original.Skip, decoded.Skip);
        Assert.Equal(original.ShowDeleted, decoded.ShowDeleted);
    }

    [Fact]
    public async Task RoundTrip_WithParent_PreservesParentValue() {
        var original = new PageToken {
            Filter = "status = 'active'", Parent = "organizations/acme", PageSize = 10,
        };

        var protector = CreateRoundTripProtector();
        var encoded = await original.ToStringAsync(protector);
        var decoded = await PageToken.FromStringAsync(encoded, protector);
        Assert.NotNull(decoded);
        Assert.Equal(original.Parent, decoded.Parent);
    }

    [Fact]
    public async Task RoundTrip_WithNullValues_PreservesNulls() {
        var original = new PageToken {
            Filter      = null,
            OrderBy     = null,
            Parent      = null,
            ShowDeleted = null,
            PageSize    = 0,
            Skip        = 0,
        };

        var protector = CreateRoundTripProtector();
        var encoded = await original.ToStringAsync(protector);
        var decoded = await PageToken.FromStringAsync(encoded, protector);
        Assert.NotNull(decoded);
        Assert.Null(decoded.Filter);
        Assert.Null(decoded.OrderBy);
        Assert.Null(decoded.Parent);
        Assert.Null(decoded.ShowDeleted);
    }

    [Fact]
    public async Task FromStringAsync_Tampered_ThrowsValidation() {
        var token     = new PageToken { Filter = "test" };
        var protector = new Mock<IDataProtector>();
        protector.Setup(p => p.Protect(It.IsAny<byte[]>())).Returns((byte[] bytes) => bytes);
        protector.Setup(p => p.Unprotect(It.IsAny<byte[]>())).Throws<CryptographicException>();
        var encoded = await token.ToStringAsync(protector.Object);

        var tampered = encoded[..^5];

        await Assert.ThrowsAsync<ValidationException>(() => PageToken.FromStringAsync(tampered, protector.Object));
    }

    [Fact]
    public async Task FromStringAsync_NotBase64_ThrowsValidation() {
        await Assert.ThrowsAsync<ValidationException>(() => PageToken.FromStringAsync("%%%not-a-token%%%", Mock.Of<IDataProtector>()));
    }

    [Fact]
    public async Task FromStringAsync_NullOrWhitespace_ReturnsNull() {
        var protector = Mock.Of<IDataProtector>();
        Assert.Null(await PageToken.FromStringAsync(null, protector));
        Assert.Null(await PageToken.FromStringAsync("   ", protector));
    }

    private static IDataProtector CreateRoundTripProtector() {
        var protector = new Mock<IDataProtector>();
        protector.Setup(p => p.Protect(It.IsAny<byte[]>())).Returns((byte[] bytes) => bytes);
        protector.Setup(p => p.Unprotect(It.IsAny<byte[]>())).Returns((byte[] bytes) => bytes);
        return protector.Object;
    }

}
