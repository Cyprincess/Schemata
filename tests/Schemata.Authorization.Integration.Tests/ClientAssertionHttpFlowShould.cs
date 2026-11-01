using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Schemata.Authorization.Integration.Tests.Fixtures;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using Xunit;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;
using static Schemata.Security.Skeleton.SecurityConstants;

namespace Schemata.Authorization.Integration.Tests;

/// <summary>
///     Issue #131 transferred #70 acceptance over real HTTP: registered
///     <c>private_key_jwt</c>/<c>client_secret_jwt</c> clients authenticate the client-credentials
///     grant through <c>client_assertion</c>, while an invalid signature, a client mismatch, and a
///     mixed assertion+secret presentation are rejected.
/// </summary>
public class ClientAssertionHttpFlowShould(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    private const string Issuer = "https://localhost";

    [Fact]
    public async Task Private_Key_Jwt_Client_Completes_Client_Credentials_Over_Http() {
        using var rsa = RSA.Create(2048);
        var clientId = await SeedPrivateKeyJwtClientAsync(rsa, "pkjwt-client");

        var response = await factory.CreateClient().SendAsync(Token(
            ("grant_type", GrantTypes.ClientCredentials),
            ("client_id", clientId),
            ("client_assertion_type", ClientAssertionTypes.JwtBearer),
            ("client_assertion", Mint(rsa, clientId))));

        var body = await response.Content.ReadAsStringAsync();
        Assert.True(HttpStatusCode.OK == response.StatusCode, $"{(int)response.StatusCode}: {body}");
        Assert.False(string.IsNullOrWhiteSpace(
            JsonDocument.Parse(body).RootElement.GetProperty("access_token").GetString()));
    }

    [Fact]
    public async Task Private_Key_Jwt_Rejects_An_Unregistered_Signing_Key() {
        using var rsa = RSA.Create(2048);
        var clientId = await SeedPrivateKeyJwtClientAsync(rsa, "pkjwt-forged-client");

        using var forged = RSA.Create(2048);
        var response = await factory.CreateClient().SendAsync(Token(
            ("grant_type", GrantTypes.ClientCredentials),
            ("client_id", clientId),
            ("client_assertion_type", ClientAssertionTypes.JwtBearer),
            ("client_assertion", Mint(forged, clientId))));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(OAuthErrors.InvalidClient,
            JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Private_Key_Jwt_Rejects_An_Assertion_For_Another_Client() {
        using var rsa = RSA.Create(2048);
        var clientId = await SeedPrivateKeyJwtClientAsync(rsa, "pkjwt-mismatch-client");

        // The assertion subject names a different client than the one the grant targets.
        var response = await factory.CreateClient().SendAsync(Token(
            ("grant_type", GrantTypes.ClientCredentials),
            ("client_id", clientId),
            ("client_assertion_type", ClientAssertionTypes.JwtBearer),
            ("client_assertion", Mint(rsa, "someone-else"))));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(OAuthErrors.InvalidClient,
            JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Assertion_Beside_A_Posted_Secret_Is_An_Assertion_Specific_Rejection() {
        using var rsa = RSA.Create(2048);
        var clientId = await SeedPrivateKeyJwtClientAsync(rsa, "pkjwt-mixed-client");

        var response = await factory.CreateClient().SendAsync(Token(
            ("grant_type", GrantTypes.ClientCredentials),
            ("client_id", clientId),
            ("client_secret", "a-secret-any-secret"),
            ("client_assertion_type", ClientAssertionTypes.JwtBearer),
            ("client_assertion", Mint(rsa, clientId))));

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(OAuthErrors.InvalidClient, body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Client_Secret_Jwt_Client_Completes_Client_Credentials_Over_Http() {
        const string secret = "csjwt-shared-secret-0123456789abcdef0123456789";
        var clientId = await SeedClientSecretJwtClientAsync(secret);
        var key      = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));

        var response = await factory.CreateClient().SendAsync(Token(
            ("grant_type", GrantTypes.ClientCredentials),
            ("client_id", clientId),
            ("client_assertion_type", ClientAssertionTypes.JwtBearer),
            ("client_assertion", Mint(key, SigningAlgorithms.HmacSha256, clientId))));

        var body = await response.Content.ReadAsStringAsync();
        Assert.True(HttpStatusCode.OK == response.StatusCode, $"{(int)response.StatusCode}: {body}");
    }

    private async Task<string> SeedPrivateKeyJwtClientAsync(RSA rsa, string clientId) {
        return await SeedClientAsync(clientId, ClientAuthMethods.PrivateKeyJwt,
            new() {
                Kind   = Kinds.Jwks,
                Usage  = Usages.Authentication,
                Status = Statuses.Valid,
                Value  = Jwks(rsa),
            });
    }

    private async Task<string> SeedClientSecretJwtClientAsync(string secret) {
        return await SeedClientAsync("csjwt-client", ClientAuthMethods.ClientSecretJwt,
            new() {
                Kind   = Kinds.Secret,
                Usage  = Usages.Authentication,
                Status = Statuses.Valid,
                Value  = secret,
            });
    }

    private async Task<string> SeedClientAsync(string clientId, string method, SchemataSecurity key) {
        await using var scope = factory.Services.CreateAsyncScope();
        var apps = scope.ServiceProvider.GetRequiredService<Skeleton.Managers.IApplicationManager<SchemataApplication>>();
        var app = new SchemataApplication {
            Name         = clientId,
            ClientId     = clientId,
            TokenEndpointAuthMethod = method,
            Permissions  = ["e:/Connect/Token"],
            GrantTypes   = [GrantTypes.ClientCredentials],
        };
        await apps.CreateAsync(app);

        var securities = scope.ServiceProvider.GetRequiredService<ISecurityStore<SchemataSecurity>>();
        key.Uid    = Guid.NewGuid();
        key.Parent = Foundation.Services.SecurityParents.Application(app);
        key.Key    = clientId;
        await securities.CreateAsync(key);

        return clientId;
    }

    private static string Mint(RSA rsa, string subject) {
        return Mint(new RsaSecurityKey(rsa) { KeyId = "integration-1" }, SigningAlgorithms.RsaSha256, subject);
    }

    private static string Mint(SecurityKey key, string algorithm, string subject) {
        var now = DateTimeOffset.UtcNow;
        var descriptor = new SecurityTokenDescriptor {
            Issuer  = subject,
            Claims  = new Dictionary<string, object> {
                ["sub"] = subject,
                ["aud"] = Issuer + Endpoints.Token,
                ["jti"] = Guid.NewGuid().ToString(),
            },
            Expires            = now.AddMinutes(5).UtcDateTime,
            NotBefore          = now.AddMinutes(-1).UtcDateTime,
            IssuedAt           = now.AddMinutes(-1).UtcDateTime,
            SigningCredentials = new(key, algorithm),
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    private static string Jwks(RSA rsa) {
        var parameters = rsa.ExportParameters(false);
        var jwk = new {
            kty = "RSA",
            kid = "integration-1",
            use = "sig",
            n   = Base64UrlEncoder.Encode(parameters.Modulus!),
            e   = Base64UrlEncoder.Encode(parameters.Exponent!),
        };
        return "{\"keys\":[" + JsonSerializer.Serialize(jwk) + "]}";
    }

    private static HttpRequestMessage Token(params (string Name, string Value)[] form) {
        var pairs = new List<KeyValuePair<string, string>>();
        foreach (var (name, value) in form) {
            pairs.Add(new(name, value));
        }
        var content = new FormUrlEncodedContent(pairs);
        return new(HttpMethod.Post, "/connect/token") {
            Content = content,
        };
    }
}
