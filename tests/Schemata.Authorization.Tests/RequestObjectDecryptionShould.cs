using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Caching.Skeleton;
using Schemata.Security.Foundation;
using Schemata.Security.Skeleton;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using Xunit;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

public sealed class RequestObjectDecryptionShould : IDisposable
{
    private const string ClientId = "encrypted-client";
    private const string Issuer = "https://as.example";
    private const string ClientKeyId = "client-signing";
    private const string IssuerKeyId = "issuer-encryption";

    private readonly RSA _clientKey = RSA.Create(2048);
    private readonly RSA _issuerKey = RSA.Create(2048);
    private readonly List<SchemataSecurity> _rows = [];

    [Fact]
    public async Task Recover_The_Exact_Inner_Compact_Jws() {
        var signed = MintSigned();
        var assertion = Encrypt(signed);

        var decrypted = await Decrypter().DecryptAsync(assertion, Application(), CancellationToken.None);

        Assert.Equal(signed, decrypted);
    }

    [Fact]
    public async Task Merge_A_Valid_Signed_Then_Encrypted_Object() {
        var target = Query();
        var assertion = Mint();
        target.Request = assertion;

        await Reader().ReadAsync(assertion, Application(), target, true, CancellationToken.None);

        Assert.Equal("openid encrypted", target.Scope);
        Assert.Equal("encrypted-state", target.State);
        Assert.Equal(assertion, target.Request);
    }

    [Fact]
    public async Task Decrypt_With_A_Retained_Issuer_Key() {
        SeedRows();
        var retired = _rows.Single(row => row.Usage == SecurityConstants.Usages.Encryption);
        retired.Status = SecurityConstants.Statuses.Retired;
        retired.RetirementTime = DateTime.UtcNow.AddMinutes(-5);
        var assertion = Mint();
        var target = Query();

        await Reader().ReadAsync(assertion, Application(), target, true, CancellationToken.None);

        Assert.Equal("encrypted-state", target.State);
    }

    [Fact]
    public async Task Reject_A_Tampered_Authentication_Tag() {
        var parts = Mint().Split('.');
        var tag = Base64UrlEncoder.DecodeBytes(parts[4]);
        tag[0] ^= 1;
        parts[4] = Base64UrlEncoder.Encode(tag);
        var assertion = string.Join('.', parts);

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            Reader().ReadAsync(assertion, Application(), Query(), true, CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidRequestObject, exception.Status);
    }

    [Theory]
    [InlineData(EncryptionAlgorithms.RsaOaep256, ContentEncryptionAlgorithms.Aes256CbcHmacSha512)]
    [InlineData(EncryptionAlgorithms.RsaOaep, ContentEncryptionAlgorithms.Aes128CbcHmacSha256)]
    public async Task Reject_An_Algorithm_Or_Content_Encryption_Not_Configured(string alg, string enc) {
        var application = Application();
        application.RequestObjectEncryptionAlg = alg;
        application.RequestObjectEncryptionEnc = enc;

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            Reader().ReadAsync(Mint(alg: alg, enc: enc), application, Query(), true, CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidRequestObject, exception.Status);
    }

    [Fact]
    public async Task Reject_An_Invalid_Inner_Signature() {
        var parts = MintSigned().Split('.');
        var signature = Base64UrlEncoder.DecodeBytes(parts[2]);
        signature[0] ^= 1;
        parts[2] = Base64UrlEncoder.Encode(signature);
        var assertion = Encrypt(string.Join('.', parts));

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            Reader().ReadAsync(assertion, Application(), Query(), true, CancellationToken.None));

        Assert.Equal(OAuthErrors.InvalidRequestObject, exception.Status);
    }

    [Fact]
    public async Task Advertise_Only_Configured_Algorithms_With_A_Valid_Key() {
        var options = Options();
        options.ContentEncryptionAlgorithms.Add(ContentEncryptionAlgorithms.Aes128CbcHmacSha256);
        var decrypter = Decrypter(options);
        var advisor = new AdviceDiscoveryRequestObject<SchemataSecurity>(decrypter, Microsoft.Extensions.Options.Options.Create(options));
        var discovery = new DiscoveryContext { Document = new() };

        await advisor.AdviseAsync(new AdviceContext(new Mock<IServiceProvider>().Object), discovery);

        Assert.NotNull(discovery.Document);
        Assert.Equal([EncryptionAlgorithms.RsaOaep], discovery.Document.RequestObjectEncryptionAlgValuesSupported);
        Assert.Equal([
            ContentEncryptionAlgorithms.Aes128CbcHmacSha256,
            ContentEncryptionAlgorithms.Aes256CbcHmacSha512,
        ], discovery.Document.RequestObjectEncryptionEncValuesSupported);
    }

    private RequestObjectReader<SchemataApplication, SchemataSecurity> Reader() {
        var options = Options();
        return new(
            Microsoft.Extensions.Options.Options.Create(options),
            Microsoft.Extensions.Options.Options.Create(new SchemataAuthorizationOptions { Issuer = Issuer }),
            Microsoft.Extensions.Options.Options.Create(new SchemataSecurityOptions()),
            HttpFactory(),
            Cache(),
            SecurityStore(),
            new(),
            decrypter: Decrypter(options));
    }

    private RequestObjectDecrypter<SchemataSecurity> Decrypter(JwtSecuredAuthorizationRequestsOptions? options = null) {
        if (_rows.Count == 0) {
            SeedRows();
        }

        return new(
            Microsoft.Extensions.Options.Options.Create(options ?? Options()),
            Microsoft.Extensions.Options.Options.Create(new SchemataAuthorizationOptions { Issuer = Issuer }),
            Microsoft.Extensions.Options.Options.Create(new SchemataSecurityOptions { RetiredKeyRetention = TimeSpan.FromDays(1) }),
            HttpFactory(),
            Cache(),
            SecurityStore());
    }

    private void SeedRows() {
        var parameters = _clientKey.ExportParameters(false);
        _rows.Add(new() {
            Parent = SecurityParents.Application(Application()),
            Kind = SecurityConstants.Kinds.Jwks,
            Usage = SecurityConstants.Usages.Authentication,
            Status = SecurityConstants.Statuses.Valid,
            Value = JsonSerializer.Serialize(new {
                keys = new[] {
                    new {
                        kty = "RSA",
                        kid = ClientKeyId,
                        use = "sig",
                        n = Base64UrlEncoder.Encode(parameters.Modulus!),
                        e = Base64UrlEncoder.Encode(parameters.Exponent!),
                    },
                },
            }),
        });
        _rows.Add(new() {
            Parent = SecurityParents.Issuer(Issuer),
            Kind = SecurityConstants.Kinds.PrivateKey,
            Usage = SecurityConstants.Usages.Encryption,
            Algorithm = SecurityConstants.Algorithms.Rsa,
            Kid = IssuerKeyId,
            Status = SecurityConstants.Statuses.Valid,
            Value = _issuerKey.ExportPkcs8PrivateKeyPem(),
        });
    }

    private ISecurityStore<SchemataSecurity> SecurityStore() {
        if (_rows.Count == 0) {
            SeedRows();
        }
        var store = new Mock<ISecurityStore<SchemataSecurity>>();
        store.Setup(value => value.ListByParentAsync(
                 It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
             .Returns((string? parent, string? kind, string? usage, string? status, CancellationToken _) =>
                 Enumerate(_rows.Where(row =>
                     (parent is null || row.Parent == parent)
                     && (kind is null || row.Kind == kind)
                     && (usage is null || row.Usage == usage)
                     && (status is null || row.Status == status))));
        return store.Object;
    }

    private static IHttpClientFactory HttpFactory() {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(value => value.CreateClient(It.IsAny<string>())).Returns(new HttpClient());
        return factory.Object;
    }

    private static ICacheProvider Cache() {
        var cache = new Mock<ICacheProvider>();
        cache.Setup(value => value.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((byte[]?)null);
        return cache.Object;
    }

    private static JwtSecuredAuthorizationRequestsOptions Options() {
        var options = new JwtSecuredAuthorizationRequestsOptions();
        options.SigningAlgorithms.Add(SigningAlgorithms.RsaSha256);
        options.EncryptionAlgorithms.Add(EncryptionAlgorithms.RsaOaep);
        options.ContentEncryptionAlgorithms.Add(ContentEncryptionAlgorithms.Aes256CbcHmacSha512);
        return options;
    }

    private static SchemataApplication Application() {
        return new() {
            ClientId = ClientId,
            CanonicalName = "applications/" + ClientId,
            RequestObjectSigningAlg = SigningAlgorithms.RsaSha256,
            RequestObjectEncryptionAlg = EncryptionAlgorithms.RsaOaep,
            RequestObjectEncryptionEnc = ContentEncryptionAlgorithms.Aes256CbcHmacSha512,
        };
    }

    private static AuthorizeRequest Query() {
        return new() {
            ClientId = ClientId,
            ResponseType = ResponseTypes.Code,
            Scope = Scopes.OpenId,
            Request = "outer-request",
        };
    }

    private string Mint(
        string alg = EncryptionAlgorithms.RsaOaep,
        string enc = ContentEncryptionAlgorithms.Aes256CbcHmacSha512
    ) {
        var assertion = Encrypt(MintSigned(), enc: enc);
        if (alg == EncryptionAlgorithms.RsaOaep) {
            return assertion;
        }

        var parts = assertion.Split('.');
        using var header = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(parts[0]));
        var values = header.RootElement.EnumerateObject().ToDictionary(
            property => property.Name,
            property => property.Name == "alg" ? alg : property.Value.GetString());
        parts[0] = Base64UrlEncoder.Encode(JsonSerializer.SerializeToUtf8Bytes(values));
        return string.Join('.', parts);
    }

    private string MintSigned() {
        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(new SecurityTokenDescriptor {
            Issuer = ClientId,
            Audience = Issuer,
            Claims = new Dictionary<string, object> {
                [Parameters.ClientId] = ClientId,
                [Parameters.ResponseType] = ResponseTypes.Code,
                [Parameters.Scope] = "openid encrypted",
                [Parameters.State] = "encrypted-state",
            },
            SigningCredentials = new(new RsaSecurityKey(_clientKey) { KeyId = ClientKeyId }, SigningAlgorithms.RsaSha256),
        });
    }

    private string Encrypt(
        string signed,
        string alg = EncryptionAlgorithms.RsaOaep,
        string enc = ContentEncryptionAlgorithms.Aes256CbcHmacSha512
    ) {
        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.EncryptToken(
            signed,
            new EncryptingCredentials(new RsaSecurityKey(_issuerKey) { KeyId = IssuerKeyId }, alg, enc),
            new Dictionary<string, object> { ["cty"] = "JWT" });
    }

    private static async IAsyncEnumerable<SchemataSecurity> Enumerate(IEnumerable<SchemataSecurity> rows) {
        await Task.Yield();
        foreach (var row in rows) {
            yield return row;
        }
    }

    public void Dispose() {
        _clientKey.Dispose();
        _issuerKey.Dispose();
    }
}
