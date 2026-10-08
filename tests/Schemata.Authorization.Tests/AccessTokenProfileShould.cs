using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Services;
using Schemata.Caching.Skeleton;
using Schemata.Security.Foundation;
using Schemata.Security.Skeleton;
using Xunit;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

/// <summary>
///     Behavioral coverage for the authenticated access-token profile validation, per
///     RFC 9068 §5: the inner signed JWT of a nested token (or the token itself when not
///     nested) must carry the <c>at+jwt</c> media type, read only after cryptographic
///     validation.
/// </summary>
public class AccessTokenProfileShould
{
    private const string Issuer = "https://as.example";

    private static TokenService CreateService() {
        return TestSecurityKeys.CreateTokenService(new() { Issuer = Issuer });
    }

    private static (TokenService Service, RsaSecurityKey EncryptionKey) CreateEncryptingService() {
        var options = new SchemataAuthorizationOptions { Issuer = Issuer };
        var store   = new TestSecurityStore();
        TestSecurityKeys.AddSigningRow(store, Issuer);

        var rsa = RSA.Create(2048);
        TestSecurityKeys.AddEncryptionRow(store, Issuer, key: rsa);

        var service = new TokenService(
            store,
            new StubHttpClientFactory(),
            new Mock<ICacheProvider>().Object,
            Options.Create(new SchemataSecurityOptions()),
            Options.Create(options));

        return (service, new(rsa));
    }

    /// <summary>Signs a JWS whose header genuinely omits <c>typ</c> when it is null — the
    /// handler default would otherwise stamp <c>JWT</c> and turn the case into a wrong-type
    /// test instead of a missing-header one.</summary>
    private static string SignRaw(TokenService service, string? typ) {
        var context = service.BeginSigningAsync().AsTask().GetAwaiter().GetResult();
        try {
            var key = (RsaSecurityKey)context.Signing.Key;
            var rsa = key.Rsa ?? RSA.Create(key.Parameters);

            var header  = typ is null ? "{\"alg\":\"RS256\"}" : $"{{\"alg\":\"RS256\",\"typ\":\"{typ}\"}}";
            var payload = $"{{\"iss\":\"{Issuer}\",\"iat\":{DateTimeOffset.UtcNow.ToUnixTimeSeconds()},\"exp\":{DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()}}}";
            var h = Base64UrlEncoder.Encode(header);
            var p = Base64UrlEncoder.Encode(payload);

            var signature = rsa.SignData(System.Text.Encoding.ASCII.GetBytes($"{h}.{p}"),
                                         HashAlgorithmName.SHA256,
                                         RSASignaturePadding.Pkcs1);

            return $"{h}.{p}.{Base64UrlEncoder.Encode(signature)}";
        } finally {
            context.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    /// <summary>Wraps a signed token as an A128CBC-HS256 JWE whose header omits <c>typ</c>,
    /// per RFC 7516 §5.2.2.2 content encryption.</summary>
    private static string WrapRawWithoutTyp(RsaSecurityKey key, string inner) {
        var rsa = key.Rsa ?? RSA.Create(key.Parameters);

        var cek = RandomNumberGenerator.GetBytes(32);
        var header      = Base64UrlEncoder.Encode("{\"alg\":\"RSA-OAEP\",\"enc\":\"A128CBC-HS256\",\"cty\":\"JWT\"}");
        var encryptedCek = Base64UrlEncoder.Encode(rsa.Encrypt(cek, RSAEncryptionPadding.OaepSHA1));
        var iv          = RandomNumberGenerator.GetBytes(16);

        using var hmac = new HMACSHA256(cek[..16]);
        using var aes  = Aes.Create();
        aes.Key   = cek[16..];
        aes.IV    = iv;
        aes.Mode  = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        byte[] ciphertext;
        using (var encryptor = aes.CreateEncryptor()) {
            ciphertext = encryptor.TransformFinalBlock(System.Text.Encoding.UTF8.GetBytes(inner), 0, inner.Length);
        }

        var al    = System.Text.Encoding.ASCII.GetBytes(header);
        var alBits = BitConverter.GetBytes((long)(al.Length * 8L));
        if (BitConverter.IsLittleEndian) Array.Reverse(alBits);
        var input = new byte[al.Length + iv.Length + ciphertext.Length + 8];
        al.CopyTo(input, 0);
        iv.CopyTo(input, al.Length);
        ciphertext.CopyTo(input, al.Length + iv.Length);
        alBits.CopyTo(input, al.Length + iv.Length + ciphertext.Length);
        var tag = hmac.ComputeHash(input)[..16];

        return $"{header}.{encryptedCek}.{Base64UrlEncoder.Encode(iv)}.{Base64UrlEncoder.Encode(ciphertext)}.{Base64UrlEncoder.Encode(tag)}";
    }

    private static string SignInner(TokenService service, string? typ) {
        var context = service.BeginSigningAsync().AsTask().GetAwaiter().GetResult();
        try {
            var now = DateTime.UtcNow;

            return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor {
                Issuer             = Issuer,
                IssuedAt           = now,
                Expires            = now.AddHours(1),
                TokenType          = typ,
                SigningCredentials = context.Signing,
            });
        } finally {
            context.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static string Wrap(TokenService service, RsaSecurityKey encryptionKey, string inner, string? outerTyp) {
        var encrypting = new EncryptingCredentials(
            encryptionKey, SecurityAlgorithms.RsaPKCS1, ContentEncryptionAlgorithms.Aes128CbcHmacSha256);
        var headers = new Dictionary<string, object>();
        if (outerTyp is not null) {
            headers["typ"] = outerTyp;
        }

        return new JsonWebTokenHandler().EncryptToken(inner, encrypting, additionalHeaderClaims: headers);
    }

    [Fact]
    public async Task Accepts_Signed_Token_With_Access_Media_Type() {
        var service = CreateService();
        var jwt     = await service.CreateToken([new("sub", "u1")], TimeSpan.FromHours(1), typ: "at+jwt");

        Assert.NotNull(await service.ValidateAccessToken(jwt));
    }

    [Fact]
    public async Task Accepts_Long_Application_Form() {
        var service = CreateService();
        var jwt     = await service.CreateToken([new("sub", "u1")], TimeSpan.FromHours(1), typ: "application/at+jwt");

        Assert.NotNull(await service.ValidateAccessToken(jwt));
    }

    [Fact]
    public async Task Accepts_Nested_Token_Minted_By_The_Service() {
        var (service, _) = CreateEncryptingService();
        var jwe = await service.CreateToken([new("sub", "u1")], TimeSpan.FromHours(1), encrypt: true, typ: "at+jwt");

        Assert.NotNull(await service.ValidateAccessToken(jwe));
    }
    [Fact]
    public async Task Rejects_Genuinely_Missing_Signed_Type_Header() {
        var service = CreateService();
        var jwt     = SignRaw(service, null);

        Assert.Null(await service.ValidateAccessToken(jwt));
    }

    [Fact]
    public async Task Rejects_Nested_Token_With_Missing_Inner_Type() {
        var (service, key) = CreateEncryptingService();
        var jwe = Wrap(service, key, SignRaw(service, null), "at+jwt");

        Assert.Null(await service.ValidateAccessToken(jwe));
    }

    [Fact]
    public async Task Genuinely_Missing_Outer_Typ_Does_Not_Reject_Correct_Inner() {
        var (service, key) = CreateEncryptingService();
        var jwe = WrapRawWithoutTyp(key, SignInner(service, "at+jwt"));
        var handler = new JsonWebTokenHandler();
        await using var context = await service.BeginSigningAsync();
        var parameters = new TokenValidationParameters {
            ValidIssuer        = Issuer,
            IssuerSigningKeys  = [context.Signing.Key],
            TokenDecryptionKey = key,
            ValidateAudience   = false,
        };
        var result = await handler.ValidateTokenAsync(jwe, parameters);
        Assert.True(result.IsValid, result.Exception?.ToString() ?? "valid");

        Assert.NotNull(await service.ValidateAccessToken(jwe));
    }

    [Fact]
    public async Task Rejects_Default_Jwt_Type_Header() {
        var service = CreateService();
        var jwt     = await service.CreateToken([new("sub", "u1")], TimeSpan.FromHours(1));

        Assert.Null(await service.ValidateAccessToken(jwt));
    }

    [Fact]
    public async Task Wrong_Inner_Type_Is_Not_Rescued_By_Outer_Header() {
        var (service, key) = CreateEncryptingService();
        var jwe = Wrap(service, key, SignInner(service, "logout+jwt"), "at+jwt");

        Assert.Null(await service.ValidateAccessToken(jwe));
    }

    [Fact]
    public async Task Default_Outer_Typ_Does_Not_Reject_Correct_Inner() {
        var (service, key) = CreateEncryptingService();
        var jwe = Wrap(service, key, SignInner(service, "at+jwt"), null);

        Assert.NotNull(await service.ValidateAccessToken(jwe));
    }

    [Fact]
    public async Task Rejects_Token_Signed_By_An_Untrusted_Key() {
        var service = CreateService();
        using var rsa = RSA.Create(2048);
        var forged = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor {
            Issuer             = Issuer,
            Expires            = DateTime.UtcNow.AddHours(1),
            TokenType          = "at+jwt",
            SigningCredentials = new(new RsaSecurityKey(rsa), SigningAlgorithms.RsaSha256),
        });

        Assert.Null(await service.ValidateAccessToken(forged));
    }

    [Fact]
    public async Task Rejects_Expired_Token() {
        var clock     = new FakeTimeProvider(new DateTimeOffset(2035, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var service   = TestSecurityKeys.CreateTokenService(new() { Issuer = Issuer }, time: clock);
        var issuedAt  = clock.GetUtcNow();
        await using var signing = await service.BeginSigningAsync();
        var jwt = service.CreateToken(
            signing, signing.Signing, [new("sub", "u1")], issuedAt, issuedAt + TimeSpan.FromMinutes(1), typ: "at+jwt");
        clock.Advance(TimeSpan.FromMinutes(3));

        Assert.Null(await service.ValidateAccessToken(jwt));
    }


    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) { return new(); }
    }
}
