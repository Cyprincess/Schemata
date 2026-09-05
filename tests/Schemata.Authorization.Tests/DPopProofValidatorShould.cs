using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Services;
using Schemata.Caching.Skeleton;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using Xunit;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

public class DPopProofValidatorShould
{
    private const string TokenUri = "https://server.example.com/token";

    private static readonly DateTimeOffset Now = new(2026, 8, 26, 0, 0, 0, TimeSpan.Zero);

    // Fixed public keys so the RFC 7638 thumbprint goldens are reproducible.
    private const string GoldenRsaN =
        "2KIf-Gl90PM3bSaO75TQvt5G3ixNWxTKrmVOV7hKs56TxVbncCA2n9WHLkJUq8BM-OmaXZCQThzvDUTWOxSANP3L1GqIcG6vZMCbte73E4iD4mxTI6V5cQgJK_ui3fmWSmoeeBuP3vOayFAgmP25UOcMDaZdNycyeHvECPgbXbjs3LgJlyO5J3BG4SbSJvJi2Jyib2GfQyyX2NdDZm-q2NvZNylKq9KovlIMm5ngVnQV0PDZAocE9-SrPStbT6BvBHDC7_weDdfw4fy5iKuqEPpf8YobEc6xDoh8xYgILeFiXRJ99ajcGUKrCpqpfCzLK5qS7WhHpJ_iC0cY2YRtlQ";
    private const string GoldenRsaE = "AQAB";

    private const string GoldenEcCrv = "P-256";
    private const string GoldenEcX   = "3uLrvSVYox6avk7J6eBIkJtLe0ZEkJ2Owqx3y5-ures";
    private const string GoldenEcY   = "5b_l5BIG50biBcvhW4mFKpFy4VS3ulQX0CUDcul8fFg";

    private static DPopProofValidator Validator(
        Mock<ICacheProvider>?             cache   = null,
        Mock<ITokenStore<SchemataToken>>? nonces  = null,
        DPopOptions?                      options = null
    ) {
        var mock = cache ?? new Mock<ICacheProvider>();
        if (cache is null) {
            Setup_TryAdd(mock, true);
        }

        return new(mock.Object, (nonces ?? new Mock<ITokenStore<SchemataToken>>()).Object, Options.Create(options ?? new DPopOptions()), new FakeTimeProvider(Now));
    }

    private static Task<string> Validate(
        DPopProofValidator validator,
        string             proof,
        string?            accessToken   = null,
        string?            nonceProvider = null,
        string             nonceName     = "client-1",
        string             htm           = "POST",
        Uri?               htu           = null
    ) {
        return validator.ValidateAsync(proof, htm, htu ?? new Uri(TokenUri), accessToken, nonceProvider, nonceName, default);
    }

    private static SigningCredentials RsaCredentials(RSA rsa, string algorithm = "RS256") {
        return new(new RsaSecurityKey(rsa), algorithm);
    }

    private static SigningCredentials EcCredentials(ECDsa ec, string algorithm = "ES256") {
        return new(new ECDsaSecurityKey(ec), algorithm);
    }

    private static Dictionary<string, object> RsaJwk(RSA rsa) {
        var parameters = rsa.ExportParameters(false);
        return new() {
            ["kty"] = "RSA",
            ["n"]   = Base64UrlEncoder.Encode(parameters.Modulus!),
            ["e"]   = Base64UrlEncoder.Encode(parameters.Exponent!),
        };
    }

    private static Dictionary<string, object> EcJwk(ECDsa ec) {
        var parameters = ec.ExportParameters(false);
        return new() {
            ["kty"] = "EC",
            ["crv"] = "P-256",
            ["x"]   = Base64UrlEncoder.Encode(parameters.Q.X!),
            ["y"]   = Base64UrlEncoder.Encode(parameters.Q.Y!),
        };
    }

    private static string Mint(
        string?             typ         = "dpop+jwt",
        string?             htm         = "POST",
        string?             htu         = TokenUri,
        DateTimeOffset?     iat         = null,
        string?             jti         = "unique",
        string?             ath         = null,
        string?             nonce       = null,
        SigningCredentials? credentials = null,
        object?             jwk         = null,
        bool                withJwk     = true
    ) {
        var claims = new Dictionary<string, object>();
        if (jti is not null) {
            claims["jti"] = jti;
        }

        if (htm is not null) {
            claims["htm"] = htm;
        }

        if (htu is not null) {
            claims["htu"] = htu;
        }

        claims["iat"] = (iat ?? Now).ToUnixTimeSeconds();
        if (ath is not null) {
            claims["ath"] = ath;
        }

        if (nonce is not null) {
            claims["nonce"] = nonce;
        }

        var descriptor = new SecurityTokenDescriptor {
            TokenType          = typ,
            Claims             = claims,
            SigningCredentials = credentials,
        };
        if (withJwk && jwk is not null) {
            descriptor.AdditionalHeaderClaims = new Dictionary<string, object> { ["jwk"] = jwk };
        }

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    // Unsigned proofs whose header/payload matter verbatim are assembled from
    // segments; the fake signature segment is never reached by the validator.
    private static string Raw(object header, object payload) {
        return Base64UrlEncoder.Encode(JsonSerializer.Serialize(header)) + "."
             + Base64UrlEncoder.Encode(JsonSerializer.Serialize(payload)) + "."
             + Base64UrlEncoder.Encode("unused-signature");
    }

    private static string ThumbprintOf(string canonicalJson) {
        return Base64UrlEncoder.Encode(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson)));
    }

    private static void Setup_TryAdd(Mock<ICacheProvider> cache, bool added) {
        cache.Setup(
            value => value.TryAddAsync(
                It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CacheEntryOptions>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync(added);
    }

    private static Mock<ITokenStore<SchemataToken>> NonceSlots(string value) {
        var slots = new Mock<ITokenStore<SchemataToken>>();
        slots.Setup(value => value.GetOrCreateAsync(
                    It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                    It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SchemataToken { Provider = "dpop", Name = "client-1", Value = value });
        return slots;
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
        cache.Setup(
                 value => value.TryAddAsync(
                     It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CacheEntryOptions>(),
                     It.IsAny<CancellationToken>()))
            .Callback((string key, byte[] _, CacheEntryOptions options, CancellationToken _) => entries.Add(options))
            .ReturnsAsync((string key, byte[] value, CacheEntryOptions _, CancellationToken _) =>
                store.TryAdd(key, value));
        return (cache, store, entries);
    }

    [Theory]
    [InlineData("not-a-jwt")]
    [InlineData("")]
    [InlineData("!!!.!!!.!!!")]
    public async Task Reject_A_Non_Jwt_Proof(string proof) {
        var cache = new Mock<ICacheProvider>();

        var ex = await Assert.ThrowsAsync<OAuthException>(() => Validate(Validator(cache), proof));

        Assert.Equal(OAuthErrors.InvalidDpopProof, ex.Status);
        cache.Verify(
            value => value.TryAddAsync(
                It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CacheEntryOptions>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
    [Theory]
    [InlineData("JWT")]
    [InlineData(null)]
    public async Task Reject_A_Wrong_Typ_Header(string? typ) {
        var rsa = RSA.Create(2048);

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => Validate(Validator(), Mint(typ: typ, credentials: RsaCredentials(rsa), jwk: RsaJwk(rsa))));

        Assert.Equal(OAuthErrors.InvalidDpopProof, ex.Status);
    }
    [Fact]
    public async Task Reject_A_Proof_Missing_A_Required_Claim() {
        var rsa = RSA.Create(2048);

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => Validate(Validator(), Mint(jti: null, credentials: RsaCredentials(rsa), jwk: RsaJwk(rsa))));

        Assert.Equal(OAuthErrors.InvalidDpopProof, ex.Status);
    }
    [Fact]
    public async Task Reject_A_Symmetric_Algorithm() {
        var rsa = RSA.Create(2048);
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32)), "HS256");

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => Validate(Validator(), Mint(credentials: credentials, jwk: RsaJwk(rsa))));

        Assert.Equal(OAuthErrors.InvalidDpopProof, ex.Status);
    }
    [Fact]
    public async Task Reject_The_None_Algorithm() {
        var rsa = RSA.Create(2048);
        var proof = Raw(
            new { alg = "none", typ = "dpop+jwt", jwk = RsaJwk(rsa) },
            new { jti = "unique", htm = "POST", htu = TokenUri, iat = Now.ToUnixTimeSeconds() });

        var ex = await Assert.ThrowsAsync<OAuthException>(() => Validate(Validator(), proof));

        Assert.Equal(OAuthErrors.InvalidDpopProof, ex.Status);
    }
    [Fact]
    public async Task Reject_An_Algorithm_Outside_The_Allow_List() {
        var rsa = RSA.Create(2048);
        var options = new DPopOptions();
        options.SigningAlgorithms.Clear();
        options.SigningAlgorithms.Add("ES256");

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => Validate(Validator(options: options), Mint(credentials: RsaCredentials(rsa), jwk: RsaJwk(rsa))));

        Assert.Equal(OAuthErrors.InvalidDpopProof, ex.Status);
    }
    [Fact]
    public async Task Reject_A_Proof_Without_A_Jwk_Header() {
        var rsa = RSA.Create(2048);

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => Validate(Validator(), Mint(withJwk: false, credentials: RsaCredentials(rsa))));

        Assert.Equal(OAuthErrors.InvalidDpopProof, ex.Status);
    }
    [Fact]
    public async Task Reject_A_Structurally_Invalid_Jwk() {
        var rsa = RSA.Create(2048);

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => Validate(Validator(), Mint(credentials: RsaCredentials(rsa), jwk: "[")));

        Assert.Equal(OAuthErrors.InvalidDpopProof, ex.Status);
    }
    [Fact]
    public async Task Reject_A_Jwk_Containing_A_Private_Rsa_Key() {
        var rsa = RSA.Create(2048);
        var jwk = RsaJwk(rsa);
        jwk["d"] = Base64UrlEncoder.Encode(rsa.ExportParameters(true).D!);

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => Validate(Validator(), Mint(credentials: RsaCredentials(rsa), jwk: jwk)));

        Assert.Equal(OAuthErrors.InvalidDpopProof, ex.Status);
    }
    [Fact]
    public async Task Reject_A_Jwk_Containing_A_Symmetric_Key() {
        var rsa = RSA.Create(2048);
        var jwk = new Dictionary<string, object> {
            ["kty"] = "oct",
            ["k"]   = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32)),
        };

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => Validate(Validator(), Mint(credentials: RsaCredentials(rsa), jwk: jwk)));

        Assert.Equal(OAuthErrors.InvalidDpopProof, ex.Status);
    }
    [Fact]
    public async Task Reject_A_Signature_From_A_Different_Key() {
        var signer  = RSA.Create(2048);
        var claimed = RSA.Create(2048);

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => Validate(Validator(), Mint(credentials: RsaCredentials(signer), jwk: RsaJwk(claimed))));

        Assert.Equal(OAuthErrors.InvalidDpopProof, ex.Status);
    }
    [Fact]
    public async Task Reject_A_Jwk_Off_The_P256_Curve() {
        var x = new byte[32];
        var y = new byte[32];
        y[0] = 0x02;
        var jwk = new Dictionary<string, object> {
            ["kty"] = "EC",
            ["crv"] = "P-256",
            ["x"]   = Base64UrlEncoder.Encode(x),
            ["y"]   = Base64UrlEncoder.Encode(y),
        };

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => Validate(
                Validator(),
                Mint(credentials: EcCredentials(ECDsa.Create(ECCurve.NamedCurves.nistP256)), jwk: jwk)));

        Assert.Equal(OAuthErrors.InvalidDpopProof, ex.Status);
    }
    [Fact]
    public async Task Reject_A_Wrong_Htm() {
        var rsa = RSA.Create(2048);

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => Validate(
                Validator(), Mint(credentials: RsaCredentials(rsa), jwk: RsaJwk(rsa)), htm: "GET"));

        Assert.Equal(OAuthErrors.InvalidDpopProof, ex.Status);
    }
    [Fact]
    public async Task Reject_An_Htu_That_Differs_From_The_Request_Uri() {
        var rsa = RSA.Create(2048);

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => Validate(
                Validator(),
                Mint(htu: "https://server.example.com/other", credentials: RsaCredentials(rsa), jwk: RsaJwk(rsa))));

        Assert.Equal(OAuthErrors.InvalidDpopProof, ex.Status);
    }
    [Fact]
    public async Task Reject_An_Htu_Claim_Carrying_A_Query() {
        var rsa = RSA.Create(2048);

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => Validate(
                Validator(),
                Mint(htu: $"{TokenUri}?grant_type=authorization_code", credentials: RsaCredentials(rsa), jwk: RsaJwk(rsa))));

        Assert.Equal(OAuthErrors.InvalidDpopProof, ex.Status);
    }
    [Theory]
    [InlineData(-31)]
    [InlineData(31)]
    public async Task Reject_A_Proof_Outside_The_Iat_Window(int offsetSeconds) {
        var rsa = RSA.Create(2048);

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => Validate(
                Validator(),
                Mint(iat: Now.AddSeconds(offsetSeconds), credentials: RsaCredentials(rsa), jwk: RsaJwk(rsa))));

        Assert.Equal(OAuthErrors.InvalidDpopProof, ex.Status);
    }
    [Fact]
    public async Task Reject_A_Replayed_Jti() {
        var cache = new Mock<ICacheProvider>();
        Setup_TryAdd(cache, false);
        var rsa = RSA.Create(2048);

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => Validate(Validator(cache), Mint(credentials: RsaCredentials(rsa), jwk: RsaJwk(rsa))));

        Assert.Equal(OAuthErrors.InvalidDpopProof, ex.Status);
    }
    [Fact]
    public async Task Reject_A_Wrong_Ath() {
        var rsa = RSA.Create(2048);
        var proof = Mint(
            ath: Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes("other-token"))),
            credentials: RsaCredentials(rsa),
            jwk: RsaJwk(rsa));

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => Validate(Validator(), proof, accessToken: "token-1"));

        Assert.Equal(OAuthErrors.InvalidDpopProof, ex.Status);
    }
    [Fact]
    public async Task Reject_A_Missing_Ath_When_An_Access_Token_Is_Presented() {
        var rsa = RSA.Create(2048);

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => Validate(Validator(), Mint(credentials: RsaCredentials(rsa), jwk: RsaJwk(rsa)), accessToken: "token-1"));

        Assert.Equal(OAuthErrors.InvalidDpopProof, ex.Status);
    }
    [Fact]
    public async Task Reject_A_Stale_Nonce_With_Use_Dpop_Nonce() {
        var (cache, _, _) = Cache();
        var nonces = NonceSlots("server-nonce");
        var rsa = RSA.Create(2048);

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => Validate(
                new(cache.Object, nonces.Object, Options.Create(new DPopOptions()), new FakeTimeProvider(Now)),
                Mint(nonce: "stale-nonce", credentials: RsaCredentials(rsa), jwk: RsaJwk(rsa)),
                nonceProvider: "dpop",
                nonceName: "client-1"));

        Assert.Equal(OAuthErrors.UseDpopNonce, ex.Status);
    }
    [Fact]
    public async Task Reject_A_Missing_Nonce_When_A_Nonce_Is_Required() {
        var (cache, _, _) = Cache();
        var nonces = NonceSlots("server-nonce");
        var rsa = RSA.Create(2048);

        var ex = await Assert.ThrowsAsync<OAuthException>(
            () => Validate(
                new(cache.Object, nonces.Object, Options.Create(new DPopOptions()), new FakeTimeProvider(Now)),
                Mint(credentials: RsaCredentials(rsa), jwk: RsaJwk(rsa)),
                nonceProvider: "dpop",
                nonceName: "client-1"));

        Assert.Equal(OAuthErrors.UseDpopNonce, ex.Status);
    }
    [Fact]
    public async Task Accept_A_Valid_Proof_And_Return_The_Key_Thumbprint() {
        var rsa = RSA.Create(2048);
        var parameters = rsa.ExportParameters(false);
        var canonical = $"{{\"e\":\"{Base64UrlEncoder.Encode(parameters.Exponent!)}\",\"kty\":\"RSA\",\"n\":\"{Base64UrlEncoder.Encode(parameters.Modulus!)}\"}}";

        var thumbprint = await Validate(Validator(), Mint(credentials: RsaCredentials(rsa), jwk: RsaJwk(rsa)));

        Assert.Equal(ThumbprintOf(canonical), thumbprint);
    }
    [Theory]
    [InlineData(TokenUri + "?grant_type=authorization_code")]
    [InlineData(TokenUri + "#fragment")]
    public async Task Accept_A_Proof_When_The_Incoming_Htu_Carries_A_Query_Or_Fragment(string incoming) {
        var rsa = RSA.Create(2048);

        var thumbprint = await Validate(
            Validator(), Mint(credentials: RsaCredentials(rsa), jwk: RsaJwk(rsa)), htu: new(incoming));

        Assert.False(string.IsNullOrWhiteSpace(thumbprint));
    }
    [Fact]
    public async Task Accept_A_Proof_When_The_Incoming_Htu_Has_A_Mixed_Case_Host() {
        var rsa = RSA.Create(2048);

        var thumbprint = await Validate(
            Validator(), Mint(credentials: RsaCredentials(rsa), jwk: RsaJwk(rsa)),
            htu: new("https://Server.Example.COM/token"));

        Assert.False(string.IsNullOrWhiteSpace(thumbprint));
    }
    [Fact]
    public async Task Accept_A_Proof_Bound_To_The_Presented_Access_Token() {
        var rsa = RSA.Create(2048);
        var proof = Mint(
            ath: Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes("token-1"))),
            credentials: RsaCredentials(rsa),
            jwk: RsaJwk(rsa));

        var thumbprint = await Validate(Validator(), proof, accessToken: "token-1");

        Assert.False(string.IsNullOrWhiteSpace(thumbprint));
    }
    [Fact]
    public async Task Accept_An_Es256_Proof_And_Return_Its_Thumbprint() {
        var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = ec.ExportParameters(false);
        var canonical = $"{{\"crv\":\"{GoldenEcCrv}\",\"kty\":\"EC\",\"x\":\"{Base64UrlEncoder.Encode(parameters.Q.X!)}\",\"y\":\"{Base64UrlEncoder.Encode(parameters.Q.Y!)}\"}}";

        var thumbprint = await Validate(
            Validator(), Mint(credentials: EcCredentials(ec), jwk: EcJwk(ec)));

        Assert.Equal(ThumbprintOf(canonical), thumbprint);
    }
    [Fact]
    public async Task Accept_A_Proof_Carrying_The_Current_Server_Nonce() {
        var (cache, _, _) = Cache();
        var nonces = NonceSlots("server-nonce");
        var validator = new DPopProofValidator(cache.Object, nonces.Object, Options.Create(new DPopOptions()), new FakeTimeProvider(Now));
        var rsa = RSA.Create(2048);

        var thumbprint = await Validate(
            validator,
            Mint(nonce: "server-nonce", credentials: RsaCredentials(rsa), jwk: RsaJwk(rsa)),
            nonceProvider: "dpop",
            nonceName: "client-1");

        Assert.False(string.IsNullOrWhiteSpace(thumbprint));
    }
    [Fact]
    public async Task Track_A_Jti_For_Its_Remaining_Acceptance_Window() {
        var captured = new List<CacheEntryOptions>();
        var cache = new Mock<ICacheProvider>();
        cache.Setup(
                 value => value.TryAddAsync(
                     It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CacheEntryOptions>(), It.IsAny<CancellationToken>()))
             .Callback<string, byte[], CacheEntryOptions, CancellationToken>((_, _, options, _) => captured.Add(options))
             .ReturnsAsync(true);
        var validator = new DPopProofValidator(cache.Object, new Mock<ITokenStore<SchemataToken>>().Object, Options.Create(new DPopOptions()), new FakeTimeProvider(Now));
        var rsa       = RSA.Create(2048);

        await Validate(validator, Mint(credentials: RsaCredentials(rsa), jwk: RsaJwk(rsa)));
        await Validate(validator, Mint(iat: Now.AddSeconds(10), jti: "other", credentials: RsaCredentials(rsa), jwk: RsaJwk(rsa)));

        Assert.Equal(2, captured.Count);
        // iat is a Unix-seconds value, so the remaining lifetime loses the sub-second
        // remainder of the clock.
        Assert.InRange(captured[0].AbsoluteExpirationRelativeToNow!.Value, TimeSpan.FromSeconds(29), TimeSpan.FromSeconds(30));
        Assert.InRange(captured[1].AbsoluteExpirationRelativeToNow!.Value, TimeSpan.FromSeconds(39), TimeSpan.FromSeconds(40));
    }
    [Fact]
    public void Compute_The_Rfc_7638_Thumbprint_Of_A_Fixed_Rsa_Key() {
        var jwk = new JsonWebKey($"{{\"kty\":\"RSA\",\"n\":\"{GoldenRsaN}\",\"e\":\"{GoldenRsaE}\"}}");
        var canonical = $"{{\"e\":\"{GoldenRsaE}\",\"kty\":\"RSA\",\"n\":\"{GoldenRsaN}\"}}";

        Assert.Equal(ThumbprintOf(canonical), DPopProofValidator.ComputeThumbprint(jwk));
    }
    [Fact]
    public void Compute_The_Rfc_7638_Thumbprint_Of_A_Fixed_Ec_Key() {
        var jwk = new JsonWebKey($"{{\"kty\":\"EC\",\"crv\":\"{GoldenEcCrv}\",\"x\":\"{GoldenEcX}\",\"y\":\"{GoldenEcY}\"}}");
        var canonical = $"{{\"crv\":\"{GoldenEcCrv}\",\"kty\":\"EC\",\"x\":\"{GoldenEcX}\",\"y\":\"{GoldenEcY}\"}}";

        Assert.Equal(ThumbprintOf(canonical), DPopProofValidator.ComputeThumbprint(jwk));
    }
}
