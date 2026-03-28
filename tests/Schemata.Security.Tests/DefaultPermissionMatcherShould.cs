using System.Security.Claims;
using Microsoft.Extensions.Options;
using Schemata.Security.Foundation;
using Xunit;

namespace Schemata.Security.Tests;

public class DefaultPermissionMatcherShould
{
    private static DefaultPermissionMatcher CreateMatcher(string? claimType = null) {
        var options = new SchemataSecurityOptions();
        if (!string.IsNullOrWhiteSpace(claimType)) {
            options.PermissionClaimType = claimType;
        }

        return new(Options.Create(options));
    }

    private static ClaimsPrincipal CreatePrincipal(string? claimType = null, params string[] permissions) {
        return CreatePrincipalWithClaims(claimType, permissions);
    }

    private static ClaimsPrincipal CreatePrincipalWithClaims(string? claimType, params string[] values) {
        if (string.IsNullOrWhiteSpace(claimType)) {
            var options = new SchemataSecurityOptions();
            claimType = options.PermissionClaimType;
        }

        var identity = new ClaimsIdentity("Test");
        foreach (var value in values) {
            identity.AddClaim(new(claimType, value));
        }

        return new(identity);
    }

    [Fact]
    public void IsMatch_ExactMatch_ReturnsTrue() {
        var matcher   = CreateMatcher();
        var principal = CreatePrincipal(null, "product.create");

        Assert.True(matcher.IsMatch(principal, "product.create"));
    }

    [Fact]
    public void IsMatch_NoMatch_ReturnsFalse() {
        var matcher   = CreateMatcher();
        var principal = CreatePrincipal(null, "product.create");

        Assert.False(matcher.IsMatch(principal, "product.delete"));
    }

    [Fact]
    public void IsMatch_WildcardOperation_ReturnsTrue() {
        var matcher   = CreateMatcher();
        var principal = CreatePrincipal(null, "product.*");

        Assert.True(matcher.IsMatch(principal, "product.create"));
    }

    [Fact]
    public void IsMatch_WildcardEntity_ReturnsTrue() {
        var matcher   = CreateMatcher();
        var principal = CreatePrincipal(null, "*.create");

        Assert.True(matcher.IsMatch(principal, "product.create"));
    }

    [Fact]
    public void IsMatch_NoClaims_ReturnsFalse() {
        var matcher   = CreateMatcher();
        var principal = new ClaimsPrincipal(new ClaimsIdentity());

        Assert.False(matcher.IsMatch(principal, "product.create"));
    }

    [Fact]
    public void IsMatch_CustomClaimType_UsesConfiguredType() {
        const string customType = "permissions";

        var matcher   = CreateMatcher(customType);
        var principal = CreatePrincipalWithClaims(customType, "product.create");

        Assert.True(matcher.IsMatch(principal, "product.create"));
    }

    [Fact]
    public void IsMatch_ThreeSegments_WildcardMiddle_ReturnsTrue() {
        var matcher   = CreateMatcher();
        var principal = CreatePrincipal(null, "ns.*.operation");

        Assert.True(matcher.IsMatch(principal, "ns.entity.operation"));
    }

    [Fact]
    public void IsMatch_ThreeSegments_WildcardEnd_ReturnsTrue() {
        var matcher   = CreateMatcher();
        var principal = CreatePrincipal(null, "ns.entity.*");

        Assert.True(matcher.IsMatch(principal, "ns.entity.operation"));
    }

    [Fact]
    public void IsMatch_FourSegments_WildcardMiddle_ReturnsTrue() {
        var matcher   = CreateMatcher();
        var principal = CreatePrincipal(null, "a.b.*.operation");

        Assert.True(matcher.IsMatch(principal, "a.b.entity.operation"));
    }

    [Fact]
    public void IsMatch_FourSegments_WildcardEnd_ReturnsTrue() {
        var matcher   = CreateMatcher();
        var principal = CreatePrincipal(null, "a.b.entity.*");

        Assert.True(matcher.IsMatch(principal, "a.b.entity.operation"));
    }

    [Fact]
    public void IsMatch_WildcardFirstSegment_ReturnsFalse() {
        var matcher   = CreateMatcher();
        var principal = CreatePrincipal(null, "*.entity.operation");

        Assert.False(matcher.IsMatch(principal, "ns.entity.operation"));
    }

    [Fact]
    public void IsMatch_GlobalWildcard_ReturnsFalse() {
        var matcher   = CreateMatcher();
        var principal = CreatePrincipal(null, "*");

        Assert.False(matcher.IsMatch(principal, "ns.entity.operation"));
    }

    [Fact]
    public void IsMatch_ThreeSegments_WildcardMiddle_OperationMismatch_ReturnsFalse() {
        var matcher   = CreateMatcher();
        var principal = CreatePrincipal(null, "ns.*.wrong");

        Assert.False(matcher.IsMatch(principal, "ns.entity.operation"));
    }

    [Fact]
    public void IsMatch_MultipleWildcards_ReturnsFalse() {
        var matcher   = CreateMatcher();
        var principal = CreatePrincipal(null, "ns.*.*.operation");

        Assert.False(matcher.IsMatch(principal, "ns.a.b.operation"));
    }
}
