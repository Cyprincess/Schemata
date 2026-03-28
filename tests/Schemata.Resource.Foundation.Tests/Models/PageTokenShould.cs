using System.Threading.Tasks;
using Schemata.Resource.Foundation.Models;
using Xunit;

namespace Schemata.Resource.Foundation.Tests.Models;

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

        var encoded = await original.ToStringAsync();
        Assert.NotNull(encoded);
        Assert.NotEmpty(encoded);

        var decoded = await PageToken.FromStringAsync(encoded);
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

        var encoded = await original.ToStringAsync();
        var decoded = await PageToken.FromStringAsync(encoded);
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

        var encoded = await original.ToStringAsync();
        var decoded = await PageToken.FromStringAsync(encoded);
        Assert.NotNull(decoded);
        Assert.Null(decoded.Filter);
        Assert.Null(decoded.OrderBy);
        Assert.Null(decoded.Parent);
        Assert.Null(decoded.ShowDeleted);
    }

    [Fact]
    public async Task FromStringAsync_WithNullToken_ReturnsNull() {
        var result = await PageToken.FromStringAsync(null);
        Assert.Null(result);
    }

    [Fact]
    public async Task FromStringAsync_WithEmptyToken_ReturnsNull() {
        var result = await PageToken.FromStringAsync("");
        Assert.Null(result);
    }

    [Fact]
    public async Task FromStringAsync_WithMalformedJson_ReturnsNull() {
        // Create a token, encode it, then tamper with the encoded string
        var token   = new PageToken { Filter = "test" };
        var encoded = await token.ToStringAsync();

        // Truncate to create invalid base64
        var tampered = encoded[..^5];

        var result = await PageToken.FromStringAsync(tampered);
        Assert.Null(result);
    }

    [Fact]
    public async Task DifferentFilters_ProduceDifferentEncodings() {
        var token1 = new PageToken { Filter = "age > 18", PageSize = 10 };
        var token2 = new PageToken { Filter = "age < 5", PageSize  = 10 };

        var encoded1 = await token1.ToStringAsync();
        var encoded2 = await token2.ToStringAsync();

        Assert.NotEqual(encoded1, encoded2);
    }
}
