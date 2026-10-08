using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Integration.Tests.Fixtures;
using Xunit;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Integration.Tests;

[Trait("Category", "Integration")]
[Trait("Layer", "Component")]
public class JwtBearerGrantShould
{
    private const string Issuer   = "https://localhost";
    private const string Identity = "https://jwt-idp.example.com";
    private const string Subject = "users/u-1";

    private static readonly DateTimeOffset Anchor = new(2026, 8, 26, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Exchange_A_Trusted_Assertion_For_An_Access_Token_For_Its_Subject() {
        using var rsa = RSA.Create(2048);
        using var factory = New_Factory(rsa);
        var client = factory.CreateClient();

        var response = await client.SendAsync(Token(new() {
            new("grant_type", GrantTypes.JwtBearer),
            new("assertion", Mint(rsa)),
            new("client_id", "jwt-client"),
            new("client_secret", "jwt-secret"),
            new("scope", "api:read"),
        }));
        Assert.True(HttpStatusCode.OK == response.StatusCode,
            $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

        var pair    = JsonDocument.Parse(await response.Content.ReadAsStreamAsync()).RootElement;
        var payload = Payload(pair.GetProperty("access_token").GetString()!);
        Assert.Equal(Subject, payload.GetProperty("sub").GetString());
        Assert.Equal("jwt-client", payload.GetProperty("client_id").GetString());
        Assert.Equal(Issuer, payload.GetProperty("aud").GetString());

        // RFC 7521 §4.1: assertion grants yield short-lived access tokens, not refresh tokens.
        Assert.False(pair.TryGetProperty("refresh_token", out _));
    }

    [Fact]
    public async Task Reject_An_Assertion_From_An_Untrusted_Issuer_With_InvalidGrant() {
        using var rsa = RSA.Create(2048);
        using var factory = new WebAppFactory().WithServices(
            services => services.AddSingleton<TimeProvider>(new FakeTimeProvider(Anchor)));
        var client = factory.CreateClient();

        var response = await client.SendAsync(Token(new() {
            new("grant_type", GrantTypes.JwtBearer),
            new("assertion", Mint(rsa)),
            new("client_id", "jwt-client"),
            new("client_secret", "jwt-secret"),
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = JsonDocument.Parse(await response.Content.ReadAsStreamAsync()).RootElement;
        Assert.Equal(OAuthErrors.InvalidGrant, error.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Discovery_Advertises_The_Jwt_Bearer_Grant() {
        using var rsa = RSA.Create(2048);
        using var factory = New_Factory(rsa);
        var client = factory.CreateClient();

        var response = await client.GetAsync("/.well-known/openid-configuration");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json   = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var grants = json.RootElement.GetProperty("grant_types_supported");
        Assert.Contains(grants.EnumerateArray(), grant => grant.GetString() == GrantTypes.JwtBearer);
    }

    private static WebAppFactory New_Factory(RSA trustedKey) {
        return new WebAppFactory().WithServices(services => {
            services.PostConfigure<SchemataAuthorizationOptions>(o => {
                o.AccessTokenFormat = TokenFormats.Jwt;
                o.AddJwtBearerTrustedIssuer(Identity, new RsaSecurityKey(trustedKey));
            });
            services.AddSingleton<TimeProvider>(new FakeTimeProvider(Anchor));
        });
    }

    private static HttpRequestMessage Token(List<KeyValuePair<string, string>> fields) {
        return new(HttpMethod.Post, "/connect/token") { Content = new FormUrlEncodedContent(fields) };
    }

    private static string Mint(RSA key) {
        var descriptor = new SecurityTokenDescriptor {
            Issuer = Identity,
            Claims = new Dictionary<string, object> {
                ["sub"] = Subject,
                ["aud"] = new[] { Issuer },
                ["jti"] = Guid.NewGuid().ToString("n"),
            },
            Expires            = Anchor.AddMinutes(5).UtcDateTime,
            NotBefore          = Anchor.AddMinutes(-1).UtcDateTime,
            IssuedAt           = Anchor.AddMinutes(-1).UtcDateTime,
            SigningCredentials = new(new RsaSecurityKey(key), SecurityAlgorithms.RsaSha256),
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    /// <summary>Decodes a JWT payload segment into its raw JSON so scalar and array shapes are visible.</summary>
    private static JsonElement Payload(string jwt) {
        using var document = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(jwt.Split('.')[1]));
        return document.RootElement.Clone();
    }
}
