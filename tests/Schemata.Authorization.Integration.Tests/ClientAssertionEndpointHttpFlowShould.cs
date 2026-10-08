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
///     D5: the device, introspection, and revocation endpoints bind and forward
///     <c>client_assertion</c>/<c>client_assertion_type</c>, and each publishes its own URL as an
///     assertion audience while authenticating. Registered <c>client_secret_jwt</c> and
///     <c>private_key_jwt</c> clients authenticate at all three endpoints with the endpoint URL as
///     <c>aud</c>; the issuer and token endpoint URLs stay accepted everywhere, and a registered
///     method or signing algorithm mismatch stays <c>invalid_client</c>.
/// </summary>
public class ClientAssertionEndpointHttpFlowShould(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    private const string Issuer = "https://localhost";

    [Fact]
    public async Task Client_Secret_Jwt_Authenticates_At_The_Introspection_Endpoint() {
        const string secret   = "csjwt-introspect-secret-0123456789abcdef0123456";
        var          clientId = await SeedClientSecretJwtClientAsync(factory, "csjwt-introspect", secret, ["e:/Connect/Introspect"]);
        var          key      = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));

        // client_id is omitted: the assertion subject identifies the client (RFC 7521 §4.2).
        var response = await factory.CreateClient().SendAsync(Post("/connect/introspect",
            ("token", "not-a-real-token"),
            ("client_assertion_type", ClientAssertionTypes.JwtBearer),
            ("client_assertion", Mint(key, SigningAlgorithms.HmacSha256, clientId, Issuer + Endpoints.Introspect))));

        var body = await response.Content.ReadAsStringAsync();
        Assert.True(HttpStatusCode.OK == response.StatusCode, $"{(int)response.StatusCode}: {body}");
        Assert.False(JsonDocument.Parse(body).RootElement.GetProperty("active").GetBoolean());
    }

    [Fact]
    public async Task Client_Secret_Jwt_Authenticates_At_The_Revocation_Endpoint() {
        using var     host      = factory.WithEnvironment("Revocation");
        const string  secret    = "csjwt-revoke-secret-0123456789abcdef0123456789";
        var           clientId  = await SeedClientSecretJwtClientAsync(host, "csjwt-revoke", secret, ["e:/Connect/Revoke"]);
        var           key       = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));

        var response = await host.CreateClient().SendAsync(Post("/connect/revoke",
            ("token", "not-a-real-token"),
            ("client_assertion_type", ClientAssertionTypes.JwtBearer),
            ("client_assertion", Mint(key, SigningAlgorithms.HmacSha256, clientId, Issuer + Endpoints.Revoke))));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Client_Secret_Jwt_Authenticates_At_The_Device_Endpoint() {
        using var    host     = factory.WithEnvironment("DeviceFlow");
        const string secret   = "csjwt-device-secret-0123456789abcdef01234567890";
        var          clientId = await SeedClientSecretJwtClientAsync(host, "csjwt-device", secret,
            ["e:/Connect/Device"], [GrantTypes.DeviceCode], "api");
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));

        var response = await host.CreateClient().SendAsync(Post("/connect/device",
            ("scope", "api"),
            ("client_assertion_type", ClientAssertionTypes.JwtBearer),
            ("client_assertion", Mint(key, SigningAlgorithms.HmacSha256, clientId, Issuer + Endpoints.Device))));

        var body = await response.Content.ReadAsStringAsync();
        Assert.True(HttpStatusCode.OK == response.StatusCode, $"{(int)response.StatusCode}: {body}");
        Assert.False(string.IsNullOrWhiteSpace(
            JsonDocument.Parse(body).RootElement.GetProperty("device_code").GetString()));
    }

    [Fact]
    public async Task Private_Key_Jwt_Authenticates_At_The_Introspection_Endpoint() {
        using var rsa      = RSA.Create(2048);
        var       clientId = await SeedPrivateKeyJwtClientAsync(factory, "pkjwt-introspect", rsa, ["e:/Connect/Introspect"]);

        var response = await factory.CreateClient().SendAsync(Post("/connect/introspect",
            ("token", "not-a-real-token"),
            ("client_id", clientId),
            ("client_assertion_type", ClientAssertionTypes.JwtBearer),
            ("client_assertion", Mint(rsa, clientId, Issuer + Endpoints.Introspect))));

        var body = await response.Content.ReadAsStringAsync();
        Assert.True(HttpStatusCode.OK == response.StatusCode, $"{(int)response.StatusCode}: {body}");
        Assert.False(JsonDocument.Parse(body).RootElement.GetProperty("active").GetBoolean());
    }

    [Fact]
    public async Task Private_Key_Jwt_Authenticates_At_The_Revocation_Endpoint() {
        using var host     = factory.WithEnvironment("Revocation");
        using var rsa      = RSA.Create(2048);
        var       clientId = await SeedPrivateKeyJwtClientAsync(host, "pkjwt-revoke", rsa, ["e:/Connect/Revoke"]);

        var response = await host.CreateClient().SendAsync(Post("/connect/revoke",
            ("token", "not-a-real-token"),
            ("client_id", clientId),
            ("client_assertion_type", ClientAssertionTypes.JwtBearer),
            ("client_assertion", Mint(rsa, clientId, Issuer + Endpoints.Revoke))));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Token_Endpoint_Audience_Remains_Accepted_At_The_Introspection_Endpoint() {
        const string secret   = "csjwt-token-aud-secret-0123456789abcdef01234567";
        var          clientId = await SeedClientSecretJwtClientAsync(factory, "csjwt-token-aud", secret, ["e:/Connect/Introspect"]);
        var          key      = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));

        // The channel accepts the issuer and the token endpoint URL at every endpoint; the
        // per-endpoint marker widens the audience set instead of replacing it.
        var response = await factory.CreateClient().SendAsync(Post("/connect/introspect",
            ("token", "not-a-real-token"),
            ("client_assertion_type", ClientAssertionTypes.JwtBearer),
            ("client_assertion", Mint(key, SigningAlgorithms.HmacSha256, clientId, Issuer + Endpoints.Token))));

        var body = await response.Content.ReadAsStringAsync();
        Assert.True(HttpStatusCode.OK == response.StatusCode, $"{(int)response.StatusCode}: {body}");
    }

    [Fact]
    public async Task Rejects_An_Assertion_When_The_Client_Registered_Another_Method() {
        const string secret = "csjwt-wrong-method-0123456789abcdef01234567890";
        await SeedClientAsync(factory, "secret-post-introspect", ClientAuthMethods.ClientSecretPost,
            new() {
                Kind   = Kinds.Secret,
                Usage  = Usages.Authentication,
                Status = Statuses.Valid,
                Value  = secret,
            }, ["e:/Connect/Introspect"]);
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));

        var response = await factory.CreateClient().SendAsync(Post("/connect/introspect",
            ("token", "not-a-real-token"),
            ("client_assertion_type", ClientAssertionTypes.JwtBearer),
            ("client_assertion", Mint(key, SigningAlgorithms.HmacSha256, "secret-post-introspect", Issuer + Endpoints.Introspect))));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(OAuthErrors.InvalidClient,
            JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Rejects_An_Algorithm_The_Client_Did_Not_Register() {
        using var rsa      = RSA.Create(2048);
        var       clientId = await SeedPrivateKeyJwtClientAsync(factory, "pkjwt-wrong-alg", rsa,
            ["e:/Connect/Introspect"], SigningAlgorithms.RsaSha384);

        // The client registered RS384; the assertion arrives signed with RS256.
        var response = await factory.CreateClient().SendAsync(Post("/connect/introspect",
            ("token", "not-a-real-token"),
            ("client_id", clientId),
            ("client_assertion_type", ClientAssertionTypes.JwtBearer),
            ("client_assertion", Mint(rsa, clientId, Issuer + Endpoints.Introspect))));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(OAuthErrors.InvalidClient,
            JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("error").GetString());
    }

    private async Task<string> SeedClientSecretJwtClientAsync(
        WebAppFactory host,
        string        clientId,
        string        secret,
        List<string>  permissions,
        List<string>? grantTypes = null,
        string?       scope      = null
    ) {
        return await SeedClientAsync(host, clientId, ClientAuthMethods.ClientSecretJwt,
            new() {
                Kind   = Kinds.Secret,
                Usage  = Usages.Authentication,
                Status = Statuses.Valid,
                Value  = secret,
            }, permissions, grantTypes, scope);
    }

    private async Task<string> SeedPrivateKeyJwtClientAsync(
        WebAppFactory host,
        string        clientId,
        RSA           rsa,
        List<string>  permissions,
        string?       signingAlg = null
    ) {
        return await SeedClientAsync(host, clientId, ClientAuthMethods.PrivateKeyJwt,
            new() {
                Kind   = Kinds.Jwks,
                Usage  = Usages.Authentication,
                Status = Statuses.Valid,
                Value  = Jwks(rsa),
            }, permissions, signingAlg: signingAlg);
    }

    private async Task<string> SeedClientAsync(
        WebAppFactory    host,
        string           clientId,
        string           method,
        SchemataSecurity key,
        List<string>     permissions,
        List<string>?    grantTypes = null,
        string?          scope      = null,
        string?          signingAlg = null
    ) {
        await using var services = host.Services.CreateAsyncScope();
        var apps = services.ServiceProvider.GetRequiredService<Skeleton.Managers.IApplicationManager<SchemataApplication>>();
        var app = new SchemataApplication {
            Name         = clientId,
            ClientId     = clientId,
            TokenEndpointAuthMethod     = method,
            TokenEndpointAuthSigningAlg = signingAlg,
            Permissions  = permissions,
            GrantTypes   = grantTypes,
            Scope        = scope,
        };
        await apps.CreateAsync(app);

        var securities = services.ServiceProvider.GetRequiredService<ISecurityStore<SchemataSecurity>>();
        key.Uid    = Guid.NewGuid();
        key.Parent = Foundation.Services.SecurityParents.Application(app);
        key.Key    = clientId;
        await securities.CreateAsync(key);

        return clientId;
    }

    private static string Mint(RSA rsa, string subject, string audience) {
        return Mint(new RsaSecurityKey(rsa) { KeyId = "integration-1" }, SigningAlgorithms.RsaSha256, subject, audience);
    }

    private static string Mint(SecurityKey key, string algorithm, string subject, string audience) {
        var now = DateTimeOffset.UtcNow;
        var descriptor = new SecurityTokenDescriptor {
            Issuer  = subject,
            Claims  = new Dictionary<string, object> {
                ["sub"] = subject,
                ["aud"] = audience,
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

    private static HttpRequestMessage Post(string uri, params (string Name, string Value)[] form) {
        var pairs = new List<KeyValuePair<string, string>>();
        foreach (var (name, value) in form) {
            pairs.Add(new(name, value));
        }
        var content = new FormUrlEncodedContent(pairs);
        return new(HttpMethod.Post, uri) {
            Content = content,
        };
    }
}
