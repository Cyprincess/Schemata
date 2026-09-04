using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Schemata.Caching.Skeleton;
using Schemata.Security.Foundation.Services;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;
using static Schemata.Security.Skeleton.SecurityConstants;

namespace Schemata.Security.Tests;

public class SchemataKeyMaterialShould
{
    [Fact]
    public async Task Private_Key_Rsa_Pem_Round_Trips_With_A_Fresh_Instance_Per_Call_Async() {
        using var rsa = RSA.Create(2048);
        var row = new SchemataSecurity { Kind = Kinds.PrivateKey, Algorithm = Algorithms.Rsa, Value = rsa.ExportPkcs8PrivateKeyPem() };

        var first  = await MaterialAsync(row);
        var second = await MaterialAsync(row);

        var key = Assert.IsType<SecurityKeyMaterial.RsaKey>(first?.Material).Key;
        Assert.NotSame(key, Assert.IsType<SecurityKeyMaterial.RsaKey>(second?.Material).Key);
        Assert.Equal(rsa.ExportRSAPrivateKey(), key.ExportRSAPrivateKey());
    }

    [Fact]
    public async Task Private_Key_Pkcs8_EC_Pem_Loads_An_EC_Key_Async() {
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var row = new SchemataSecurity { Kind = Kinds.PrivateKey, Algorithm = Algorithms.P256, Value = ec.ExportPkcs8PrivateKeyPem() };

        var key = Assert.IsType<SecurityKeyMaterial.EcKey>((await MaterialAsync(row))?.Material).Key;

        Assert.Equal(ec.ExportECPrivateKey(), key.ExportECPrivateKey());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("unknown")]
    [InlineData(Algorithms.X25519)]
    [InlineData("ed25519")]
    public async Task Private_Key_Row_Without_An_Importable_Algorithm_Throws_Async(string? algorithm) {
        using var rsa = RSA.Create(2048);
        var row = new SchemataSecurity { Kind = Kinds.PrivateKey, Algorithm = algorithm, Value = rsa.ExportPkcs8PrivateKeyPem() };

        var ex = await Assert.ThrowsAsync<NotSupportedException>(() => MaterialAsync(row));

        Assert.Contains(algorithm ?? "<null>", ex.Message);
    }

    [Fact]
    public async Task Private_Key_Row_With_A_Non_Pem_Value_Throws_Async() {
        var row = new SchemataSecurity { Kind = Kinds.PrivateKey, Algorithm = Algorithms.Rsa, Value = "not a pem" };

        await Assert.ThrowsAsync<NotSupportedException>(() => MaterialAsync(row));
    }

    [Fact]
    public async Task Private_Key_Row_Whose_Value_Mismatches_Its_Declared_Algorithm_Throws_Async() {
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var row = new SchemataSecurity { Kind = Kinds.PrivateKey, Algorithm = Algorithms.Rsa, Value = ec.ExportPkcs8PrivateKeyPem() };

        await Assert.ThrowsAsync<NotSupportedException>(() => MaterialAsync(row));
    }

    [Fact]
    public async Task Public_Key_Rows_Stay_Pem_Async() {
        using var rsa = RSA.Create(2048);
        var pem = rsa.ExportSubjectPublicKeyInfoPem();
        var row = new SchemataSecurity { Kind = Kinds.PublicKey, Value = pem };

        var material = Assert.IsType<SecurityKeyMaterial.PublicKeyPem>((await MaterialAsync(row))?.Material);

        Assert.Equal(pem, material.Pem);
    }

    [Fact]
    public async Task Jwk_And_Jwks_Rows_Pass_Through_Json_Async() {
        var jwk  = new SchemataSecurity { Kind = Kinds.Jwk,  Value = """{"kty":"EC","crv":"P-256"}""" };
        var jwks = new SchemataSecurity { Kind = Kinds.Jwks, Value = """{"keys":[]}""" };

        Assert.Equal(
            jwk.Value,
            Assert.IsType<SecurityKeyMaterial.JwkJson>((await MaterialAsync(jwk))?.Material).Json);
        Assert.Equal(
            jwks.Value,
            Assert.IsType<SecurityKeyMaterial.JwksJson>((await MaterialAsync(jwks))?.Material).Json);
    }

    [Fact]
    public async Task Uri_Rows_Fetch_Then_Cache_With_The_Captured_Ttl_Async() {
        var row = new SchemataSecurity {
            CanonicalName = "securities/issuer-jwks",
            Kind          = Kinds.JwksUri,
            Value         = "https://issuer.example/jwks",
        };
        var ttl = TimeSpan.FromMinutes(15);

        var handler = new CountingHandler("""{"keys":[]}""");
        var (cache, store, entries) = Cache();
        using var http = new HttpClient(handler);

        var first  = await row.ToKeyMaterialAsync(http, cache.Object, ttl);
        var second = await row.ToKeyMaterialAsync(http, cache.Object, ttl);

        Assert.Equal(row.Value, handler.LastUri);
        Assert.Equal("""{"keys":[]}""", Assert.IsType<SecurityKeyMaterial.JwksJson>(first?.Material).Json);
        Assert.Equal(1, handler.Calls);
        Assert.Equal(
            $"security-keys\x1e{row.CanonicalName ?? row.Uid.ToString()}".ToCacheKey(Keys.Authorization),
            Assert.Single(store).Key);
        var options = Assert.Single(entries);
        Assert.Equal(ttl, options.AbsoluteExpirationRelativeToNow);
        Assert.Equal("""{"keys":[]}""", Assert.IsType<SecurityKeyMaterial.JwksJson>(second?.Material).Json);
    }

    [Fact]
    public async Task Secret_Rows_Load_Utf8_Symmetric_Bytes_Async() {
        var row = new SchemataSecurity { Kind = Kinds.Secret, Value = "s3cret" };

        var material = Assert.IsType<SecurityKeyMaterial.Symmetric>((await MaterialAsync(row))?.Material);

        Assert.Equal(Encoding.UTF8.GetBytes("s3cret"), material.Key);
    }

    [Fact]
    public async Task Certificate_And_Unknown_Rows_Return_Null_Async() {
        var certificate = new SchemataSecurity { Kind = Kinds.Certificate, Value = "-----BEGIN CERTIFICATE-----" };
        var unknown     = new SchemataSecurity { Kind = "token-signing",    Value = "anything" };

        Assert.Null(await MaterialAsync(certificate));
        Assert.Null(await MaterialAsync(unknown));
    }

    [Fact]
    public async Task Rows_With_A_Blank_Value_Return_Null_Async() {
        var blank = new SchemataSecurity { Kind = Kinds.Secret, Value = " " };

        Assert.Null(await MaterialAsync(blank));
    }

    private static async Task<SchemataKeyMaterial?> MaterialAsync(SchemataSecurity row) {
        using var http = new HttpClient();
        return await row.ToKeyMaterialAsync(http, new Mock<ICacheProvider>().Object, TimeSpan.FromMinutes(5));
    }

    private static (
        Mock<ICacheProvider>       Cache,
        Dictionary<string, byte[]> Store,
        List<CacheEntryOptions>    Entries
    ) Cache() {
        var store   = new Dictionary<string, byte[]>();
        var entries = new List<CacheEntryOptions>();
        var cache   = new Mock<ICacheProvider>();
        cache.Setup(value => value.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((string key, CancellationToken _) =>
                 store.TryGetValue(key, out var bytes) ? bytes : null);
        cache.Setup(value => value.SetAsync(
                        It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CacheEntryOptions>(),
                        It.IsAny<CancellationToken>()))
             .Callback((string key, byte[] value, CacheEntryOptions options, CancellationToken _) => {
                 store[key]   = value;
                 entries.Add(options);
             })
             .Returns(Task.CompletedTask);
        return (cache, store, entries);
    }

    private sealed class CountingHandler(string body) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        public string? LastUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) {
            Calls++;
            LastUri = request.RequestUri?.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
