using System;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Models;
using Schemata.Caching.Skeleton;
using Schemata.Security.Foundation;
using Schemata.Security.Skeleton;
using Schemata.Security.Skeleton.Entities;
using Xunit;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

[Trait("Layer", "Integration")]
public class IdTokenProtectionShould
{
    private static readonly JsonWebTokenHandler Handler = new();

    [Fact]
    public async Task Encrypt_A_Complete_Signed_Token_With_Default_Content_Algorithm() {
        var store = new TestSecurityStore();
        using var clientKey = RSA.Create(2048);
        var application = await SeedAsync(store, clientKey, EncryptionAlgorithms.RsaOaep);
        var signed = SignedToken();

        var protectedToken = await Create(store).ProtectAsync(application, signed, default);

        var outer = new JsonWebToken(protectedToken);
        Assert.Equal(EncryptionAlgorithms.RsaOaep, outer.Alg);
        Assert.Equal(ContentEncryptionAlgorithms.Aes128CbcHmacSha256, outer.Enc);
        Assert.Equal("JWT", outer.Cty);
        using var wrongKey = RSA.Create(2048);
        Assert.ThrowsAny<SecurityTokenException>(() => Handler.DecryptToken(outer, new TokenValidationParameters {
            TokenDecryptionKey = new RsaSecurityKey(wrongKey),
        }));
        var decrypted = Handler.DecryptToken(outer, new TokenValidationParameters {
            TokenDecryptionKey = new RsaSecurityKey(clientKey),
        });
        Assert.Equal(signed, decrypted);
    }

    [Fact]
    public async Task Reject_Missing_Client_Key_Without_Returning_Signed_Plaintext() {
        var store = new TestSecurityStore();
        var application = new SchemataApplication {
            Name = "client", ClientId = "client", CanonicalName = "applications/client",
            IdTokenEncryptedResponseAlg = EncryptionAlgorithms.RsaOaep,
        };
        var signed = SignedToken();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Create(store).ProtectAsync(application, signed, default));

        Assert.Contains("jwks", error.Message);
        Assert.DoesNotContain(signed, error.Message);
    }

    [Theory]
    [InlineData(EncryptionAlgorithms.RsaOaep256, EncryptionAlgorithms.RsaOaep)]
    [InlineData("unsupported", EncryptionAlgorithms.RsaOaep)]
    public async Task Reject_Incompatible_Or_Unsupported_Recipient_Algorithms(string registered, string keyAlgorithm) {
        var store = new TestSecurityStore();
        using var clientKey = RSA.Create(2048);
        var application = await SeedAsync(store, clientKey, registered, keyAlgorithm);
        var signed = SignedToken();

        await Assert.ThrowsAnyAsync<Exception>(
            () => Create(store).ProtectAsync(application, signed, default));
    }

    private static string SignedToken() {
        using var key = RSA.Create(2048);
        return Handler.CreateToken(new SecurityTokenDescriptor {
            Claims = new System.Collections.Generic.Dictionary<string, object> {
                ["sub"] = "users/alice", ["nonce"] = "n-1", ["at_hash"] = "hash",
            },
            SigningCredentials = new(new RsaSecurityKey(key), SecurityAlgorithms.RsaSha256),
        });
    }

    private static async Task<SchemataApplication> SeedAsync(
        TestSecurityStore store,
        RSA clientKey,
        string registered,
        string? keyAlgorithm = null
    ) {
        var application = new SchemataApplication {
            Name = "client", ClientId = "client", CanonicalName = "applications/client",
            IdTokenEncryptedResponseAlg = registered,
        };
        var key = JsonWebKeyConverter.ConvertFromRSASecurityKey(new RsaSecurityKey(clientKey));
        await store.CreateAsync(new SchemataSecurity {
            Parent = SecurityParents.Application(application), Kind = SecurityConstants.Kinds.Jwks,
            Status = SecurityConstants.Statuses.Valid,
            Value = JsonSerializer.Serialize(new {
                keys = new object[] {
                    new { kty = key.Kty, n = key.N, e = key.E, use = "enc", alg = keyAlgorithm ?? registered },
                },
            }),
        });
        return application;
    }

    private static IdTokenResponseProtector<SchemataSecurity> Create(TestSecurityStore store) {
        var http = new Mock<IHttpClientFactory>();
        http.Setup(h => h.CreateClient(It.IsAny<string>())).Returns(new HttpClient());
        var resolver = new ClientEncryptionKeyResolver<SchemataSecurity>(
            store, http.Object, Mock.Of<ICacheProvider>(),
            Microsoft.Extensions.Options.Options.Create(new SchemataSecurityOptions()));
        return new(resolver);
    }
}

[Trait("Layer", "Integration")]
public class RegistrationIdTokenEncryptionShould
{
    [Fact]
    public async Task Default_Content_Algorithm_And_Project_Registration_Metadata() {
        var request = Request();
        request.IdTokenEncryptedResponseAlg = EncryptionAlgorithms.RsaOaep;

        var application = await RegistrationMetadataMapper.ToApplicationAsync<SchemataApplication>(
            request, Options, Http);
        var response = await RegistrationMetadataMapper.ToResponse(application, new TestSecurityStore());

        Assert.Equal(EncryptionAlgorithms.RsaOaep, application.IdTokenEncryptedResponseAlg);
        Assert.Equal(ContentEncryptionAlgorithms.Aes128CbcHmacSha256, application.IdTokenEncryptedResponseEnc);
        Assert.Equal(ContentEncryptionAlgorithms.Aes128CbcHmacSha256, response.IdTokenEncryptedResponseEnc);
    }

    [Fact]
    public async Task Reject_Content_Encryption_Without_Key_Management() {
        var request = Request();
        request.IdTokenEncryptedResponseEnc = ContentEncryptionAlgorithms.Aes256Gcm;

        await Assert.ThrowsAsync<OAuthException>(() => RegistrationMetadataMapper.ToApplicationAsync<SchemataApplication>(
            request, Options, Http));
    }

    private static RegisterRequest Request() => new() {
        RedirectUris = ["https://rp.example/cb"], GrantTypes = [GrantTypes.AuthorizationCode],
        ResponseTypes = [ResponseTypes.Code],
    };

    private static readonly Microsoft.Extensions.Options.IOptions<SchemataAuthorizationOptions> Options =
        Microsoft.Extensions.Options.Options.Create(new SchemataAuthorizationOptions().PermitResponseType(ResponseTypes.Code));
    private static readonly IHttpClientFactory Http = new StubFactory();

    private sealed class StubFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
