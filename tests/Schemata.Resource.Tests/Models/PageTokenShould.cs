using System.Threading.Tasks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Schemata.Abstractions.Exceptions;
using Schemata.Resource.Foundation.Models;
using Xunit;

namespace Schemata.Resource.Tests.Models;

public class PageTokenShould
{
    private static readonly IDataProtector Protector
        = new EphemeralDataProtectionProvider(NullLoggerFactory.Instance).CreateProtector(PageToken.ProtectionPurpose);

    [Fact]
    public async Task RoundTrip_PreservesAllFields() {
        var original = new PageToken {
            Filter      = "age > 18",
            OrderBy     = "name DESC",
            PageSize    = 25,
            Skip        = 50,
            ShowDeleted = false,
        };

        var encoded = await original.ToStringAsync(Protector);
        Assert.NotNull(encoded);
        Assert.NotEmpty(encoded);

        var decoded = await PageToken.FromStringAsync(encoded, Protector);
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

        var encoded = await original.ToStringAsync(Protector);
        var decoded = await PageToken.FromStringAsync(encoded, Protector);
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

        var encoded = await original.ToStringAsync(Protector);
        var decoded = await PageToken.FromStringAsync(encoded, Protector);
        Assert.NotNull(decoded);
        Assert.Null(decoded.Filter);
        Assert.Null(decoded.OrderBy);
        Assert.Null(decoded.Parent);
        Assert.Null(decoded.ShowDeleted);
    }

    [Fact]
    public async Task FromStringAsync_Tampered_ThrowsValidation() {
        var token   = new PageToken { Filter = "test" };
        var encoded = await token.ToStringAsync(Protector);

        var tampered = encoded[..^5];

        await Assert.ThrowsAsync<ValidationException>(() => PageToken.FromStringAsync(tampered, Protector));
    }

    [Fact]
    public async Task FromStringAsync_NotBase64_ThrowsValidation() {
        await Assert.ThrowsAsync<ValidationException>(() => PageToken.FromStringAsync("%%%not-a-token%%%", Protector));
    }

    [Fact]
    public async Task FromStringAsync_NullOrWhitespace_ReturnsNull() {
        Assert.Null(await PageToken.FromStringAsync(null, Protector));
        Assert.Null(await PageToken.FromStringAsync("   ", Protector));
    }

    [Fact]
    public async Task ToStringAsync_DoesNotLeakFieldValues() {
        var token   = new PageToken { Filter = "very-secret-filter", PageSize = 10 };
        var encoded = await token.ToStringAsync(Protector);

        Assert.DoesNotContain("very-secret-filter", encoded);
    }

    [Fact]
    public async Task DifferentFilters_ProduceDifferentEncodings() {
        var token1 = new PageToken { Filter = "age > 18", PageSize = 10 };
        var token2 = new PageToken { Filter = "age < 5", PageSize  = 10 };

        var encoded1 = await token1.ToStringAsync(Protector);
        var encoded2 = await token2.ToStringAsync(Protector);

        Assert.NotEqual(encoded1, encoded2);
    }
}
