using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Schemata.Authorization.Foundation;
using Schemata.Authorization.Foundation.Authentication;
using Xunit;

namespace Schemata.Authorization.Tests;

public class CanonicalIssuerShould
{
    [Theory]
    [InlineData("https://localhost")]
    [InlineData("https://localhost/")]
    [InlineData("https://localhost/issuer1")]
    [InlineData("https://auth.example.com:8443/tenant/one")]
    public void Accept_Root_And_Path_Issuers_Verbatim(string issuer) {
        var uri = CanonicalIssuer.Validate(issuer);

        Assert.Equal(issuer, uri.OriginalString);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-an-uri")]
    [InlineData("issuer1/path")]
    [InlineData("http://localhost")]
    [InlineData("https://user@localhost")]
    [InlineData("https://localhost/?x=1")]
    [InlineData("https://localhost/issuer1?x=1")]
    [InlineData("https://localhost#section")]
    [InlineData("https://localhost/issuer1#section")]
    [InlineData("https://localhost/issuer1/")]
    public void Reject_Non_Canonical_Issuers(string? issuer) {
        Assert.Throws<InvalidOperationException>(() => CanonicalIssuer.Validate(issuer));
    }

    [Theory]
    [InlineData("https://localhost",
                "/.well-known/openid-configuration",
                "/.well-known/oauth-authorization-server",
                "/.well-known/jwks")]
    [InlineData("https://localhost/",
                "/.well-known/openid-configuration",
                "/.well-known/oauth-authorization-server",
                "/.well-known/jwks")]
    [InlineData("https://localhost/issuer1",
                "/issuer1/.well-known/openid-configuration",
                "/.well-known/oauth-authorization-server/issuer1",
                "/issuer1/.well-known/jwks")]
    public void Derive_The_Well_Known_Routes_From_The_Issuer(
        string value,
        string openIdConfiguration,
        string oauthAuthorizationServer,
        string jwks
    ) {
        var issuer = CanonicalIssuer.Validate(value);

        Assert.Equal(openIdConfiguration, CanonicalIssuer.OpenIdConfiguration(issuer));
        Assert.Equal(oauthAuthorizationServer, CanonicalIssuer.OAuthAuthorizationServer(issuer));
        Assert.Equal(jwks, CanonicalIssuer.Jwks(issuer));
    }

    [Theory]
    [InlineData("https://localhost", "/Connect/Token", "https://localhost/Connect/Token")]
    [InlineData("https://localhost/", "/Connect/Token", "https://localhost/Connect/Token")]
    [InlineData("https://localhost/issuer1", "/Connect/Token", "https://localhost/issuer1/Connect/Token")]
    public void Combine_Endpoint_Urls_Without_A_Double_Slash(string issuer, string endpoint, string expected) {
        Assert.Equal(expected, CanonicalIssuer.Combine(issuer, endpoint));
    }

    [Fact]
    public void Reject_An_Invalid_Issuer_When_The_Options_Are_Resolved() {
        var services = new ServiceCollection();
        services.AddSchemataAuthorizationOptions(options => options.Issuer = "https://localhost/issuer1/");

        using var provider = services.BuildServiceProvider();

        Assert.Throws<InvalidOperationException>(
            () => provider.GetRequiredService<IOptions<SchemataAuthorizationOptions>>().Value);
    }

    [Fact]
    public void Preserve_The_Exact_Configured_Issuer_When_The_Options_Are_Resolved() {
        var services = new ServiceCollection();
        services.AddSchemataAuthorizationOptions(options => options.Issuer = "https://localhost/issuer1");

        using var provider = services.BuildServiceProvider();

        Assert.Equal(
            "https://localhost/issuer1",
            provider.GetRequiredService<IOptions<SchemataAuthorizationOptions>>().Value.Issuer);
    }
}
