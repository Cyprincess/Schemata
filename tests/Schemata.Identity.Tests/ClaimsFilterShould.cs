using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Schemata.Common;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Identity.Tests;

public class ClaimsFilterShould
{
    private static readonly List<Claim> AllClaims = [
        new(Claims.Subject, "42"), new(Claims.Name, "Alice Smith"), new(Claims.PreferredUsername, "alice@example.com"),
        new(Claims.Email, "alice@example.com"), new(Claims.EmailVerified, "true"),
        new(Claims.PhoneNumber, "+15551234567"), new(Claims.PhoneNumberVerified, "true"), new(Claims.Role, "admin"),
    ];

    [Fact]
    public void Filter_AlwaysIncludesSub() {
        var result = ClaimsFilter.Filter(AllClaims, []).ToList();

        Assert.Single(result);
        Assert.Equal(Claims.Subject, result[0].Type);
    }

    [Fact]
    public void Filter_ProfileScope_IncludesProfileClaims() {
        var result = ClaimsFilter.Filter(AllClaims, ["profile"]).ToList();

        Assert.Contains(result, c => c.Type == Claims.Subject);
        Assert.Contains(result, c => c.Type == Claims.Name);
        Assert.Contains(result, c => c.Type == Claims.PreferredUsername);
        Assert.DoesNotContain(result, c => c.Type == Claims.Email);
    }

    [Fact]
    public void Filter_EmailScope_IncludesEmailClaims() {
        var result = ClaimsFilter.Filter(AllClaims, ["email"]).ToList();

        Assert.Contains(result, c => c.Type == Claims.Subject);
        Assert.Contains(result, c => c.Type == Claims.Email);
        Assert.Contains(result, c => c.Type == Claims.EmailVerified);
        Assert.DoesNotContain(result, c => c.Type == Claims.Name);
    }

    [Fact]
    public void Filter_PhoneScope_IncludesPhoneClaims() {
        var result = ClaimsFilter.Filter(AllClaims, ["phone"]).ToList();

        Assert.Contains(result, c => c.Type == Claims.Subject);
        Assert.Contains(result, c => c.Type == Claims.PhoneNumber);
        Assert.Contains(result, c => c.Type == Claims.PhoneNumberVerified);
        Assert.DoesNotContain(result, c => c.Type == Claims.Email);
        Assert.DoesNotContain(result, c => c.Type == Claims.Name);
    }

    [Fact]
    public void Filter_EmptyScopes_OnlyReturnsSub() {
        var result = ClaimsFilter.Filter(AllClaims, []).ToList();

        Assert.Single(result);
        Assert.Equal(Claims.Subject, result[0].Type);
    }

    [Fact]
    public void Filter_UnknownScope_Ignored() {
        var result = ClaimsFilter.Filter(AllClaims, ["nonexistent_scope"]).ToList();

        Assert.Single(result);
        Assert.Equal(Claims.Subject, result[0].Type);
    }
}
