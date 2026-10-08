using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Commands;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Skeleton.Contexts;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Caching.Skeleton;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using Xunit;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

public class AdviceAuthorizeDpopJktShould
{
    /// <summary>The dpop_jkt example value from RFC 9449 §10 Figure 25.</summary>
    private const string Thumbprint = "NzbLsXh8uDCcd-6MNwXF4W_7noWXFZAfHkxZsRGC9Xs";

    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    private const string Issuer = "https://issuer.example";
    private const string ParUri = Issuer + Endpoints.Par;

    [Fact]
    public async Task Continue_With_The_Committed_Key_Recorded_On_The_Request() {
        var (advisor, ctx, authz, _, _) = Create(Thumbprint);

        var result = await advisor.AdviseAsync(ctx, authz);

        Assert.Equal(AdviseResult.Continue, result);
        Assert.Equal(Thumbprint, authz.Request!.DpopJkt);
    }

    [Fact]
    public async Task Continue_When_The_Parameter_Is_Absent() {
        var (advisor, ctx, authz, _, _) = Create(null);

        var result = await advisor.AdviseAsync(ctx, authz);

        Assert.Equal(AdviseResult.Continue, result);
    }

    [Theory]
    [InlineData("not-a-thumbprint!")]
    [InlineData("a+b/c=d")]
    public async Task Reject_A_Parameter_Outside_The_Base64url_Alphabet(string jkt) {
        var (advisor, ctx, authz, _, _) = Create(jkt);

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            advisor.AdviseAsync(ctx, authz));

        Assert.Equal(OAuthErrors.InvalidRequest, exception.Status);
        Assert.Equal(400, exception.Code);
    }

    [Theory]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public async Task Reject_A_Parameter_That_Does_Not_Decode_To_32_Bytes(string jkt) {
        var (advisor, ctx, authz, _, _) = Create(jkt);

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            advisor.AdviseAsync(ctx, authz));

        Assert.Equal(OAuthErrors.InvalidRequest, exception.Status);
        Assert.Equal(400, exception.Code);
    }

    [Fact]
    public async Task Derive_The_Thumbprint_From_A_Valid_Proof_Header_On_The_Par_Endpoint() {
        var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var jkt = ThumbprintOf(ec);
        var (advisor, ctx, authz, _, _) = Create(null);
        authz.Stage = AuthorizationRequestStage.Pushed;
        authz.DpopProof = Mint(ec, htu: ParUri);

        var result = await advisor.AdviseAsync(ctx, authz);

        Assert.Equal(AdviseResult.Continue, result);
        Assert.Equal(jkt, authz.Request!.DpopJkt);
    }

    [Fact]
    public async Task Accept_When_The_Proof_Thumbprint_Matches_The_Explicit_Parameter() {
        var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var jkt = ThumbprintOf(ec);
        var (advisor, ctx, authz, _, _) = Create(jkt);
        authz.Stage = AuthorizationRequestStage.Pushed;
        authz.DpopProof = Mint(ec, htu: ParUri);

        var result = await advisor.AdviseAsync(ctx, authz);

        Assert.Equal(AdviseResult.Continue, result);
        Assert.Equal(jkt, authz.Request!.DpopJkt);
    }

    [Fact]
    public async Task Reject_A_Proof_Thumbprint_That_Differs_From_The_Explicit_Parameter() {
        var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var (advisor, ctx, authz, _, _) = Create(Thumbprint);
        authz.Stage = AuthorizationRequestStage.Pushed;
        authz.DpopProof = Mint(signer, htu: ParUri);

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            advisor.AdviseAsync(ctx, authz));

        Assert.Equal(OAuthErrors.InvalidDpopProof, exception.Status);
    }

    [Fact]
    public async Task Reject_A_Proof_With_Wrong_Htu() {
        var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var (advisor, ctx, authz, _, _) = Create(null);
        authz.Stage = AuthorizationRequestStage.Pushed;
        authz.DpopProof = Mint(ec, htu: $"{Issuer}{Endpoints.Token}");

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            advisor.AdviseAsync(ctx, authz));

        Assert.Equal(OAuthErrors.InvalidDpopProof, exception.Status);
    }

    [Fact]
    public async Task Reject_A_Proof_With_Wrong_Htm() {
        var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var (advisor, ctx, authz, _, _) = Create(null);
        authz.Stage = AuthorizationRequestStage.Pushed;
        authz.DpopProof = Mint(ec, htm: "GET");

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            advisor.AdviseAsync(ctx, authz));

        Assert.Equal(OAuthErrors.InvalidDpopProof, exception.Status);
    }

    [Fact]
    public async Task Reject_A_Proof_With_A_Tampered_Signature() {
        var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var proof = Mint(ec, htu: ParUri);
        var tampered = TamperSignature(proof);
        var (advisor, ctx, authz, _, _) = Create(null);
        authz.Stage = AuthorizationRequestStage.Pushed;
        authz.DpopProof = tampered;

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            advisor.AdviseAsync(ctx, authz));

        Assert.Equal(OAuthErrors.InvalidDpopProof, exception.Status);
    }

    [Fact]
    public async Task Ignore_A_Proof_Outside_Par_And_Keep_The_Valid_Parameter() {
        var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var proof = Mint(ec, htu: $"{Issuer}{Endpoints.Token}");
        var (advisor, ctx, authz, _, _) = Create(Thumbprint);
        ctx.Set(new DpopProof(proof));

        var result = await advisor.AdviseAsync(ctx, authz);

        Assert.Equal(AdviseResult.Continue, result);
        Assert.Equal(Thumbprint, authz.Request!.DpopJkt);
    }

    private static (
        AdviceAuthorizeDpopJkt<SchemataApplication> Advisor,
        AdviceContext Ctx,
        AuthorizeContext<SchemataApplication> Authz,
        ECDsa Ec,
        string Jkt
    ) Create(string? dpopJkt) {
        var ctx   = new AdviceContext(new ServiceCollection().BuildServiceProvider());
        var authz = new AuthorizeContext<SchemataApplication> {
            Request = new() { DpopJkt = dpopJkt },
            Application = new() { ClientId = "client-1" },
        };
        var cache = new Mock<ICacheProvider>();
        cache.Setup(value => value.TryAddAsync(
                    It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CacheEntryOptions>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(true);
        var nonces = new Mock<ITokenStore<SchemataToken>>();
        var proofs = new DPopProofValidator(cache.Object, nonces.Object, Options.Create(new DPopOptions()), new FakeTimeProvider(Now));
        var server = Options.Create(new SchemataAuthorizationOptions { Issuer = Issuer });
        var advisor = new AdviceAuthorizeDpopJkt<SchemataApplication>(proofs, server);
        var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (advisor, ctx, authz, ec, ThumbprintOf(ec));
    }

    private static string Mint(
        ECDsa ec,
        string htu  = ParUri,
        string htm  = "POST",
        string? jti = null
    ) {
        var q = ec.ExportParameters(false).Q;
        var jwk = new Dictionary<string, object> {
            ["kty"] = "EC",
            ["crv"] = "P-256",
            ["x"]   = Base64UrlEncoder.Encode(q.X!),
            ["y"]   = Base64UrlEncoder.Encode(q.Y!),
        };
        var claims = new Dictionary<string, object> {
            ["jti"] = jti ?? Guid.NewGuid().ToString(),
            ["htm"] = htm,
            ["htu"] = htu,
            ["iat"] = Now.ToUnixTimeSeconds(),
        };
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor {
            TokenType              = TokenMediaTypes.DpopJwt,
            Claims                 = claims,
            SigningCredentials     = new(new ECDsaSecurityKey(ec), "ES256"),
            AdditionalHeaderClaims = new Dictionary<string, object> { ["jwk"] = jwk },
        });
    }

    private static string TamperSignature(string proof) {
        var parts = proof.Split('.');
        var signature = Base64UrlEncoder.DecodeBytes(parts[2]);
        signature[0] ^= 0xff;
        return $"{parts[0]}.{parts[1]}.{Base64UrlEncoder.Encode(signature)}";
    }

    private static string ThumbprintOf(ECDsa ec) {
        var q = ec.ExportParameters(false).Q;
        var canonical = $"{{\"crv\":\"P-256\",\"kty\":\"EC\",\"x\":\"{Base64UrlEncoder.Encode(q.X!)}\",\"y\":\"{Base64UrlEncoder.Encode(q.Y!)}\"}}";
        return Base64UrlEncoder.Encode(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}