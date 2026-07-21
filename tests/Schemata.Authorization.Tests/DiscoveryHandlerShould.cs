using System;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Handlers;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;

namespace Schemata.Authorization.Tests;

public class DiscoveryHandlerShould
{
    private static readonly string[] RsaPrivateMembers  = ["d", "p", "q", "dp", "dq", "qi", "oth"];
    private static readonly string[] EcdsaPrivateMembers = ["d"];
    private static readonly string[] SharedSecretMembers = ["k", "key_ops"];

    // Mirrors the options wired by SchemataJsonSerializerFeature so assertions cover the real wire.
    private static readonly JsonSerializerOptions WireOptions = new() {
        DictionaryKeyPolicy    = JsonNamingPolicy.SnakeCaseLower,
        PropertyNamingPolicy   = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static DiscoveryHandler<SchemataScope> CreateHandler(SecurityKey? signingKey, string? algorithm) {
        var opts = Options.Create(new SchemataAuthorizationOptions {
            SigningKey = signingKey, SigningAlgorithm = algorithm,
        });

        return new(opts, new Mock<IScopeManager<SchemataScope>>().Object, new Mock<IServiceProvider>().Object);
    }

    private static JsonElement GetOnlyKey(AuthorizationResult result) {
        var json = JsonSerializer.Serialize(result.Data, WireOptions);
        using var doc = JsonDocument.Parse(json);

        var keys = doc.RootElement.GetProperty("keys");
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

    [Fact]
    public void PublishesOnlyPublicParameters_WhenSigningKeyIsRsa() {
        using var rsa = RSA.Create(2048);
        var handler = CreateHandler(new RsaSecurityKey(rsa), SigningAlgorithms.RsaSha256);

        var entry = GetOnlyKey(handler.GetJwks());

        Assert.Equal(["kty", "use", "alg", "kid", "n", "e"], MemberNames(entry));
        Assert.Equal("RSA", entry.GetProperty("kty").GetString());
        Assert.Equal("sig", entry.GetProperty("use").GetString());
        Assert.Equal(SigningAlgorithms.RsaSha256, entry.GetProperty("alg").GetString());
        Assert.Equal(JsonValueKind.Null, entry.GetProperty("kid").ValueKind);

        var parameters = rsa.ExportParameters(false);
        Assert.Equal(Base64UrlEncoder.Encode(parameters.Modulus!), entry.GetProperty("n").GetString());
        Assert.Equal(Base64UrlEncoder.Encode(parameters.Exponent!), entry.GetProperty("e").GetString());

        AssertNoPrivateMaterial(entry, RsaPrivateMembers);
    }

    [Fact]
    public void PublishesOnlyPublicParameters_WhenSigningKeyIsRsaParameters() {
        using var rsa = RSA.Create(2048);
        var handler = CreateHandler(new RsaSecurityKey(rsa.ExportParameters(true)), SigningAlgorithms.RsaSha256);

        var entry = GetOnlyKey(handler.GetJwks());

        Assert.Equal(["kty", "use", "alg", "kid", "n", "e"], MemberNames(entry));

        var parameters = rsa.ExportParameters(false);
        Assert.Equal(Base64UrlEncoder.Encode(parameters.Modulus!), entry.GetProperty("n").GetString());
        Assert.Equal(Base64UrlEncoder.Encode(parameters.Exponent!), entry.GetProperty("e").GetString());

        AssertNoPrivateMaterial(entry, RsaPrivateMembers);
    }

    [Fact]
    public void PublishesOnlyPublicParameters_WhenSigningKeyIsEcdsa() {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var key = new ECDsaSecurityKey(ecdsa) { KeyId = "ec-key-1" };
        var handler = CreateHandler(key, SigningAlgorithms.EcdsaSha256);

        var entry = GetOnlyKey(handler.GetJwks());

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
    public void PublishesLeafCertificate_WhenSigningKeyIsX509() {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("cn=jwks-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        var handler = CreateHandler(new X509SecurityKey(certificate), SigningAlgorithms.RsaSha256);

        var entry = GetOnlyKey(handler.GetJwks());

        Assert.Equal(["kty", "use", "alg", "kid", "n", "e", "x5c", "x5t#S256"], MemberNames(entry));
        Assert.Equal("RSA", entry.GetProperty("kty").GetString());
        Assert.Equal("sig", entry.GetProperty("use").GetString());
        Assert.Equal(SigningAlgorithms.RsaSha256, entry.GetProperty("alg").GetString());
        Assert.Equal(certificate.Thumbprint, entry.GetProperty("kid").GetString());

        var parameters = rsa.ExportParameters(false);
        Assert.Equal(Base64UrlEncoder.Encode(parameters.Modulus!), entry.GetProperty("n").GetString());
        Assert.Equal(Base64UrlEncoder.Encode(parameters.Exponent!), entry.GetProperty("e").GetString());

        var x5c = entry.GetProperty("x5c");
        Assert.Equal(1, x5c.GetArrayLength());
        Assert.Equal(Convert.ToBase64String(certificate.RawData), x5c[0].GetString());

        Assert.Equal(Base64UrlEncoder.Encode(SHA256.HashData(certificate.RawData)), entry.GetProperty("x5t#S256").GetString());
        Assert.False(entry.TryGetProperty("x5t", out _), "SHA-1 'x5t' must not be emitted alongside 'x5t#S256'");

        AssertNoPrivateMaterial(entry, RsaPrivateMembers);
    }

    [Fact]
    public void ReturnsEmptyKeys_WhenSigningKeyIsSymmetric() {
        var key = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32));
        var handler = CreateHandler(key, SigningAlgorithms.HmacSha256);

        var json = JsonSerializer.Serialize(handler.GetJwks().Data, WireOptions);
        using var doc = JsonDocument.Parse(json);

        Assert.Equal(0, doc.RootElement.GetProperty("keys").GetArrayLength());
    }

    [Fact]
    public void ThrowsNotSupported_WhenSigningKeyIsJsonWebKey() {
        using var rsa = RSA.Create(2048);
        var jwk = JsonWebKeyConverter.ConvertFromSecurityKey(new RsaSecurityKey(rsa));
        var handler = CreateHandler(jwk, SigningAlgorithms.RsaSha256);

        Assert.Throws<NotSupportedException>(() => handler.GetJwks());
    }

    [Fact]
    public void ThrowsNotSupported_WhenSigningKeyIsUnknownType() {
        var handler = CreateHandler(new Mock<SecurityKey>().Object, SigningAlgorithms.RsaSha256);

        Assert.Throws<NotSupportedException>(() => handler.GetJwks());
    }

    [Fact]
    public void ThrowsInvalidOperation_WhenSigningKeyNotConfigured() {
        var handler = CreateHandler(null, null);

        Assert.Throws<InvalidOperationException>(() => handler.GetJwks());
    }
}
