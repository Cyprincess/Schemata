using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.DataProtection;
using Schemata.Abstractions;
using Schemata.Abstractions.Errors;
using Schemata.Abstractions.Exceptions;
using Schemata.Common;
using Schemata.Resource.Foundation.Models;
using Xunit;

namespace Schemata.Resource.Tests.Models;

[Trait("Category", "Integration")]
[Trait("Layer", "Integration")]
public class PageTokenShould
{
    [Fact]
    public async Task Preserve_Query_And_Continuation_Fields() {
        var original = new PageToken {
            Parent = "organizations/acme", Filter = "age > 18", Language = "aip",
            OrderBy = "name DESC", PageSize = 25, Skip = 50, ShowDeleted = false,
        };
        var protector = new EphemeralDataProtectionProvider().CreateProtector(PageToken.ProtectionPurpose);
        var encoded = await original.ToStringAsync(protector);
        var decoded = await PageToken.FromStringAsync(encoded, protector);
        Assert.NotNull(decoded);
        Assert.Equal(original.Parent, decoded.Parent);
        Assert.Equal(original.Filter, decoded.Filter);
        Assert.Equal(original.Language, decoded.Language);
        Assert.Equal(original.OrderBy, decoded.OrderBy);
        Assert.Equal(original.PageSize, decoded.PageSize);
        Assert.Equal(original.Skip, decoded.Skip);
        Assert.Equal(original.ShowDeleted, decoded.ShowDeleted);
    }

    [Theory]
    [InlineData("purpose")]
    [InlineData("key-ring")]
    [InlineData("tamper")]
    [InlineData("truncate")]
    [InlineData("negative")]
    [InlineData("unprotected")]
    [InlineData("format")]
    public async Task Reject_Invalid_Continuations_With_Field_Violations(string change) {
        var provider = new EphemeralDataProtectionProvider();
        var protector = provider.CreateProtector(PageToken.ProtectionPurpose);
        var encoded = ProtectedContinuation.Encode(protector, new PageToken { Skip = change == "negative" ? -1 : 25 });
        if (change == "purpose") protector = provider.CreateProtector("different-purpose");
        if (change == "key-ring") protector = new EphemeralDataProtectionProvider().CreateProtector(PageToken.ProtectionPurpose);
        if (change == "tamper") encoded = (encoded[0] == 'A' ? "B" : "A") + encoded[1..];
        if (change == "truncate") encoded = encoded[..^8];
        if (change == "unprotected") encoded = Convert.ToBase64String(new byte[8]);
        if (change == "format") encoded = "%%%not-a-token%%%";
        var error = await Assert.ThrowsAsync<ValidationException>(async () => await PageToken.FromStringAsync(encoded, protector));
        var field = Assert.Single(error.Details!.OfType<BadRequestDetail>()).FieldViolations;
        Assert.Equal(SchemataResources.INVALID_PAGE_TOKEN, Assert.Single(field!).Reason);
        Assert.Equal("page_token", Assert.Single(field!).Field);
    }

    [Fact]
    public async Task Preserve_Null_Parameters_And_Empty_Continuation() {
        var protector = new EphemeralDataProtectionProvider().CreateProtector(PageToken.ProtectionPurpose);
        var encoded = await new PageToken().ToStringAsync(protector);
        var decoded = await PageToken.FromStringAsync(encoded, protector);
        Assert.NotNull(decoded);
        Assert.Null(decoded.Parent);
        Assert.Null(decoded.Filter);
        Assert.Null(decoded.Language);
        Assert.Null(decoded.OrderBy);
        Assert.Null(decoded.ShowDeleted);
        Assert.Null(await PageToken.FromStringAsync(null, protector));
        Assert.Null(await PageToken.FromStringAsync("   ", protector));
    }
}
