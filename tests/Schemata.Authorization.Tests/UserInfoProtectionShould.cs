using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Caching.Skeleton;
using Schemata.Security.Foundation;
using Schemata.Security.Skeleton;
using Schemata.Security.Skeleton.Entities;
using Xunit;

namespace Schemata.Authorization.Tests;

[Trait("Layer", "Integration")]
public class UserInfoProtectionShould
{
    private const string Issuer = "https://issuer.example";

    [Fact]
    public async Task Sign_With_Client_Algorithm_And_Preserve_Structured_Claims() {
        var store = new TestSecurityStore();
        var ec = TestSecurityKeys.AddSigningRow(store, Issuer, SecurityConstants.Algorithms.P384);
        var primary = TestSecurityKeys.AddSigningRow(store, Issuer);
        primary.CreateTime = ec.CreateTime!.Value.AddMinutes(1);
        var app = new SchemataApplication { ClientId = "client", Name = "client", UserinfoSignedResponseAlg = "ES384" };
        var protector = Create(app, store);

        var result = await protector.ProtectAsync(app.ClientId, new Dictionary<string, object> {
            ["sub"] = "users/alice",
            ["email_verified"] = true,
            ["age"] = 42,
            ["address"] = JsonSerializer.Deserialize<JsonElement>("{\"country\":\"NZ\"}"),
            ["roles"] = new[] { "reader", "writer" },
        });

        Assert.NotNull(result);
        var token = new JsonWebToken(result.Value);
        Assert.Equal("ES384", token.Alg);
        Assert.Equal(ec.Kid, token.Kid);
        Assert.True(token.GetPayloadValue<bool>("email_verified"));
        Assert.Equal(42, token.GetPayloadValue<int>("age"));
        Assert.Equal("NZ", token.GetPayloadValue<JsonElement>("address").GetProperty("country").GetString());
        Assert.Equal(new[] { "reader", "writer" }, token.GetPayloadValue<string[]>("roles"));
        Assert.NotNull(await TestSecurityKeys.CreateTokenService(new() { Issuer = Issuer }, store, seed: false).Validate(result.Value));
    }

    [Fact]
    public async Task Encrypt_With_Valid_Recipient_And_Default_Content_Algorithm() {
        var store = new TestSecurityStore();
        TestSecurityKeys.AddSigningRow(store, Issuer);
        using var oldKey = RSA.Create(2048);
        using var currentKey = RSA.Create(2048);
        using var weakKey = RSA.Create(1024);
        var weakPublic = JsonWebKeyConverter.ConvertFromRSASecurityKey(new RsaSecurityKey(weakKey));
        var app = new SchemataApplication { ClientId = "client", Name = "client", CanonicalName = "applications/client", UserinfoEncryptedResponseAlg = "RSA-OAEP" };
        var oldPublic = JsonWebKeyConverter.ConvertFromRSASecurityKey(new RsaSecurityKey(oldKey));
        await store.CreateAsync(new SchemataSecurity {
            Parent = "applications/client", Kind = SecurityConstants.Kinds.Jwks, Status = SecurityConstants.Statuses.Revoked,
            Value = JsonSerializer.Serialize(new { keys = new[] { new { kty = oldPublic.Kty, n = oldPublic.N, e = oldPublic.E } } }),
        });
        var publicKey = JsonWebKeyConverter.ConvertFromRSASecurityKey(new RsaSecurityKey(currentKey));
        publicKey.D = publicKey.DP = publicKey.DQ = publicKey.P = publicKey.Q = publicKey.QI = null;
        await store.CreateAsync(new SchemataSecurity {
            Parent = "applications/client", Kind = SecurityConstants.Kinds.Jwks, Status = SecurityConstants.Statuses.Valid,
            Value = JsonSerializer.Serialize(new { keys = new object[] {
                new { kty = oldPublic.Kty, n = oldPublic.N, e = oldPublic.E, use = "sig" },
                new { kty = oldPublic.Kty, n = oldPublic.N, e = oldPublic.E, alg = "RSA-OAEP-256" },
                new { kty = oldPublic.Kty, n = oldPublic.N, e = oldPublic.E, key_ops = new[] { "verify" } },
                new { kty = weakPublic.Kty, n = weakPublic.N, e = weakPublic.E },
                new { kty = publicKey.Kty, n = publicKey.N, e = publicKey.E },
            } }),
        });
        var result = await Create(app, store).ProtectAsync(app.ClientId, new Dictionary<string, object> { ["sub"] = "users/alice" });
        Assert.NotNull(result);
        var token = new JsonWebToken(result.Value);
        Assert.Equal("A128CBC-HS256", token.Enc);
        var validated = await new JsonWebTokenHandler().ValidateTokenAsync(result.Value, new TokenValidationParameters {
            TokenDecryptionKey = new RsaSecurityKey(currentKey), RequireSignedTokens = false,
            ValidateAudience = false, ValidateIssuer = false, ValidateLifetime = true,
        });
        Assert.True(validated.IsValid, validated.Exception?.ToString());
        Assert.Equal("users/alice", validated.ClaimsIdentity.FindFirst("sub")?.Value);
    }

    private static UserInfoResponseProtector<SchemataApplication> Create(SchemataApplication app, TestSecurityStore store) {
        var apps = new Mock<IApplicationManager<SchemataApplication>>();
        apps.Setup(a => a.FindByClientIdAsync(app.ClientId!, It.IsAny<CancellationToken>())).ReturnsAsync(app);
        var http = new Mock<IHttpClientFactory>();
        http.Setup(h => h.CreateClient(It.IsAny<string>())).Returns(new HttpClient());
        var options = Options.Create(new SchemataAuthorizationOptions { Issuer = Issuer });
        return new(apps.Object, TestSecurityKeys.CreateTokenService(options.Value, store, seed: false), store,
            http.Object, Mock.Of<ICacheProvider>(), Options.Create(new SchemataSecurityOptions()), options);
    }
}
