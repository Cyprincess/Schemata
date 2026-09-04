using System;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Handlers;
using Schemata.Caching.Skeleton;
using Schemata.Security.Foundation;
using Schemata.Security.Skeleton;
using Schemata.Security.Skeleton.Entities;
using Xunit;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

public class JwksHandlerShould
{
    private const string Issuer = "https://as.example";

    private static readonly string[] RsaPrivateMembers   = ["d", "p", "q", "dp", "dq", "qi", "oth"];
    private static readonly string[] EcdsaPrivateMembers = ["d"];
    private static readonly string[] SharedSecretMembers = ["k", "key_ops"];

    // Mirrors the options wired by SchemataJsonSerializerFeature so assertions cover the real wire.
    private static readonly JsonSerializerOptions WireOptions = new() {
        DictionaryKeyPolicy    = JsonNamingPolicy.SnakeCaseLower,
        PropertyNamingPolicy   = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public async Task PublishesOnlyPublicParameters_WhenSigningRowIsRsa() {
        using var rsa = RSA.Create(2048);
        var store = new TestSecurityStore();
        var row = TestSecurityKeys.AddSigningRow(store, Issuer, key: rsa);

        var entry = await GetOnlyKeyAsync(store);

        Assert.Equal(["kty", "use", "alg", "kid", "n", "e"], MemberNames(entry));
        Assert.Equal("RSA", entry.GetProperty("kty").GetString());
        Assert.Equal("sig", entry.GetProperty("use").GetString());
        Assert.Equal(SigningAlgorithms.RsaSha256, entry.GetProperty("alg").GetString());
        Assert.Equal(row.Kid, entry.GetProperty("kid").GetString());

        rsa.ImportFromPem(row.Value);
        var parameters = rsa.ExportParameters(false);
        Assert.Equal(Base64UrlEncoder.Encode(parameters.Modulus!), entry.GetProperty("n").GetString());
        Assert.Equal(Base64UrlEncoder.Encode(parameters.Exponent!), entry.GetProperty("e").GetString());

        AssertNoPrivateMaterial(entry, RsaPrivateMembers);
    }

    [Fact]
    public async Task PublishesOnlyPublicParameters_WhenSigningRowIsEcdsa() {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var row = new SchemataSecurity {
            Parent     = Issuer,
            Name       = "ec-signing",
            Kind       = SecurityConstants.Kinds.PrivateKey,
            Usage      = SecurityConstants.Usages.Signing,
            Algorithm  = SecurityConstants.Algorithms.P256,
            Kid        = "ec-key-1",
            Value      = ecdsa.ExportPkcs8PrivateKeyPem(),
            Status     = SecurityConstants.Statuses.Valid,
            CreateTime = DateTime.UtcNow,
        };
        var store = new TestSecurityStore();
        await store.CreateAsync(row);

        var entry = await GetOnlyKeyAsync(store);

        Assert.Equal(["kty", "use", "alg", "kid", "crv", "x", "y"], MemberNames(entry));
        Assert.Equal("EC", entry.GetProperty("kty").GetString());
        Assert.Equal("sig", entry.GetProperty("use").GetString());
        Assert.Equal(SigningAlgorithms.EcdsaSha256, entry.GetProperty("alg").GetString());
        Assert.Equal("ec-key-1", entry.GetProperty("kid").GetString());
        Assert.Equal("P-256", entry.GetProperty("crv").GetString());

        var parameters = ecdsa.ExportParameters(false);
        Assert.Equal(Base64UrlEncoder.Encode(parameters.Q.X!), entry.GetProperty("x").GetString());
        Assert.Equal(Base64UrlEncoder.Encode(parameters.Q.Y!), entry.GetProperty("y").GetString());

        AssertNoPrivateMaterial(entry, EcdsaPrivateMembers);
    }

    [Fact]
    public async Task Publishes_Retired_Rows_And_Excludes_Revoked_Rows() {
        var store = new TestSecurityStore();
        var valid = TestSecurityKeys.AddSigningRow(store, Issuer);
        var retired = TestSecurityKeys.AddSigningRow(store, Issuer);
        retired.Status = SecurityConstants.Statuses.Retired;
        var revoked = TestSecurityKeys.AddSigningRow(store, Issuer);
        revoked.Status = SecurityConstants.Statuses.Revoked;

        using var json = await GetWireAsync(store);
        var kids = json.RootElement.GetProperty("keys").EnumerateArray()
            .Select(key => key.GetProperty("kid").GetString()).ToList();

        Assert.Equal(2, kids.Count);
        Assert.Contains(valid.Kid, kids);
        Assert.Contains(retired.Kid, kids);
        Assert.DoesNotContain(revoked.Kid, kids);
    }

    [Fact]
    public async Task ReturnsEmptyKeys_WhenSigningRowIsSymmetric() {
        var store = new TestSecurityStore();
        await store.CreateAsync(new() {
            Parent     = Issuer,
            Name       = "hmac-signing",
            Kind       = SecurityConstants.Kinds.Secret,
            Usage      = SecurityConstants.Usages.Signing,
            Algorithm  = SigningAlgorithms.HmacSha256,
            Value      = "shared-secret-material",
            Status     = SecurityConstants.Statuses.Valid,
            CreateTime = DateTime.UtcNow,
        });

        using var json = await GetWireAsync(store);

        Assert.Equal(0, json.RootElement.GetProperty("keys").GetArrayLength());
    }

    [Fact]
    public async Task ThrowsInvalidOperation_WhenNoSigningRowIsConfigured() {
        var handler = CreateHandler(new());

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.ExecuteAsync());
    }

    [Fact]
    public async Task Publishes_Every_Signing_Row_With_Distinct_Kids() {
        var store = new TestSecurityStore();
        var secondary = TestSecurityKeys.AddSigningRow(store, Issuer);
        var primary   = TestSecurityKeys.AddSigningRow(store, Issuer);
        secondary.CreateTime = DateTime.UtcNow;
        primary.CreateTime   = DateTime.UtcNow - TimeSpan.FromSeconds(1);

        using var json = await GetWireAsync(store);
        var keys = json.RootElement.GetProperty("keys");

        Assert.Equal(2, keys.GetArrayLength());
        var kids = keys.EnumerateArray().Select(key => key.GetProperty("kid").GetString()).ToList();
        Assert.Equal([secondary.Kid, primary.Kid], kids);
        Assert.All(keys.EnumerateArray(), key => {
            Assert.Equal("sig", key.GetProperty("use").GetString());
            Assert.Equal(SigningAlgorithms.RsaSha256, key.GetProperty("alg").GetString());
        });
    }

    private static JwksHandler CreateHandler(TestSecurityStore store) {
        return new(
            store,
            new StubHttpClientFactory(),
            new Mock<ICacheProvider>().Object,
            Options.Create(new SchemataSecurityOptions()),
            Options.Create(new SchemataAuthorizationOptions { Issuer = Issuer }));
    }

    private static async Task<JsonDocument> GetWireAsync(TestSecurityStore store) {
        var result = await CreateHandler(store).ExecuteAsync();
        var json   = JsonSerializer.Serialize(result.Data, WireOptions);
        return JsonDocument.Parse(json);
    }

    private static async Task<JsonElement> GetOnlyKeyAsync(TestSecurityStore store) {
        using var json = await GetWireAsync(store);

        var keys = json.RootElement.GetProperty("keys");
        Assert.Equal(1, keys.GetArrayLength());

        return keys[0].Clone();
    }

    private static string[] MemberNames(JsonElement entry) {
        return [..entry.EnumerateObject().Select(p => p.Name)];
    }

    private static void AssertNoPrivateMaterial(JsonElement entry, params string[] members) {
        foreach (var member in members.Concat(SharedSecretMembers)) {
            Assert.False(entry.TryGetProperty(member, out _), $"JWKS entry must not contain '{member}'");
        }
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) { return new(); }
    }
}
