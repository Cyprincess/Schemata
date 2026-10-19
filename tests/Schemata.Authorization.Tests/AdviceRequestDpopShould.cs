using System;
using System.Collections.Generic;
using System.Linq;
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
using Schemata.Authorization.Skeleton.Models;
using Schemata.Caching.Skeleton;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using Xunit;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Tests;

public class AdviceRequestDpopShould
{
    private static readonly DateTimeOffset Now = new(2026, 8, 26, 0, 0, 0, TimeSpan.Zero);

    private static readonly string TokenUri = $"https://issuer.example{Endpoints.Token}";

    private const string ServerNonce = "server-nonce";

    [Fact]
    public async Task Continue_When_The_Proof_Header_Is_Absent() {
        var advisor = Advisor(out var ctx, out var request);

        var result = await advisor.AdviseAsync(ctx, request);

        Assert.Equal(AdviseResult.Continue, result);
        Assert.Null(request.Binding);
    }
    [Fact]
    public async Task Bind_The_Proof_Key_Thumbprint_For_A_Valid_Proof() {
        var advisor = Advisor(out var ctx, out var request);

        var (proof, jkt) = Proof(ServerNonce);
        With_Header(request, proof);

        var result = await advisor.AdviseAsync(ctx, request);

        Assert.Equal(AdviseResult.Continue, result);
        Assert.Equal(jkt, request.Binding?.Jkt);
    }
    [Fact]
    public async Task Bind_The_Proof_Key_Thumbprint_For_An_Es256_Proof() {
        var advisor = Advisor(out var ctx, out var request);

        var (proof, jkt) = EcProof(ServerNonce);
        With_Header(request, proof);

        var result = await advisor.AdviseAsync(ctx, request);

        Assert.Equal(AdviseResult.Continue, result);
        Assert.Equal(jkt, request.Binding?.Jkt);
    }
    [Fact]
    public async Task Challenge_A_Proof_Missing_The_Server_Nonce() {
        var advisor = Advisor(out var ctx, out var request);

        var (proof, _) = Proof(null);
        With_Header(request, proof);

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            advisor.AdviseAsync(ctx, request));

        Assert.Equal(OAuthErrors.UseDpopNonce, exception.Status);
        Assert.Equal(400, exception.Code);
        Assert.Equal(ServerNonce, exception.Headers![Headers.DpopNonce]);
    }
    [Fact]
    public async Task Reject_A_Malformed_Proof_With_Bad_Request() {
        var advisor = Advisor(out var ctx, out var request);
        With_Header(request, "not-a-jwt");

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            advisor.AdviseAsync(ctx, request));

        Assert.Equal(OAuthErrors.InvalidDpopProof, exception.Status);
        Assert.Equal(400, exception.Code);
    }
    [Fact]
    public async Task Reject_A_Proof_Less_Request_From_A_Bound_Client() {
        var advisor = Advisor(out var ctx, out var request);
        request.Application!.DpopBoundAccessTokens = true;

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            advisor.AdviseAsync(ctx, request));

        Assert.Equal(OAuthErrors.InvalidRequest, exception.Status);
        Assert.Equal(400, exception.Code);
    }
    [Fact]
    public async Task Reject_A_Proof_Less_Request_When_The_Host_Requires_Proofs_From_All_Clients() {
        var advisor = Advisor(out var ctx, out var request, new DPopOptions().RequireForAllClients());

        var exception = await Assert.ThrowsAsync<OAuthException>(() =>
            advisor.AdviseAsync(ctx, request));

        Assert.Equal(OAuthErrors.InvalidRequest, exception.Status);
        Assert.Equal(400, exception.Code);
    }

    private static AdviceRequestDpop<SchemataApplication, SchemataToken> Advisor(
        out AdviceContext                 ctx,
        out TokenRequestContext<SchemataApplication> request,
        DPopOptions?                      dpop = null
    ) {
        var options = new SchemataAuthorizationOptions { Issuer = "https://issuer.example" };
        var cache   = Cache();
        var slots   = NonceSlots();
        var proofs  = new DPopProofValidator<SchemataToken>(cache, slots.Object, Options.Create(dpop ?? new DPopOptions()), new FakeTimeProvider(Now));
        ctx = new(new ServiceCollection().BuildServiceProvider());
        request = new() {
            Request     = new() { GrantType = GrantTypes.AuthorizationCode, Code = "code-1" },
            Application = new() { ClientId = "client-1" },
        };
        return new(proofs, slots.Object, Options.Create(options), Options.Create(dpop ?? new DPopOptions()));
    }


    private static void With_Header(TokenRequestContext<SchemataApplication> request, string proof) {
        request.Proof = proof;
    }

    private static (string Proof, string Jkt) Proof(string? nonce) {
        var rsa        = RSA.Create(2048);
        var parameters = rsa.ExportParameters(false);
        return Mint(
            new() {
                ["kty"] = "RSA",
                ["n"]   = Base64UrlEncoder.Encode(parameters.Modulus!),
                ["e"]   = Base64UrlEncoder.Encode(parameters.Exponent!),
            },
            new(new RsaSecurityKey(rsa), "RS256"),
            nonce);
    }

    private static (string Proof, string Jkt) EcProof(string? nonce) {
        var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var q  = ec.ExportParameters(false).Q;
        return Mint(
            new() {
                ["kty"] = "EC",
                ["crv"] = "P-256",
                ["x"]   = Base64UrlEncoder.Encode(q.X!),
                ["y"]   = Base64UrlEncoder.Encode(q.Y!),
            },
            new(new ECDsaSecurityKey(ec), "ES256"),
            nonce);
    }

    private static (string Proof, string Jkt) Mint(
        Dictionary<string, object> jwk,
        SigningCredentials         credentials,
        string?                    nonce
    ) {
        var claims = new Dictionary<string, object> {
            ["jti"] = Guid.NewGuid().ToString(),
            ["htm"] = "POST",
            ["htu"] = TokenUri,
            ["iat"] = Now.ToUnixTimeSeconds(),
        };
        if (nonce is not null) {
            claims["nonce"] = nonce;
        }

        var proof = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor {
            TokenType              = TokenMediaTypes.DpopJwt,
            Claims                 = claims,
            SigningCredentials     = credentials,
            AdditionalHeaderClaims = new Dictionary<string, object> { ["jwk"] = jwk },
        });

        // RFC 7638: the required JWK members in lexicographic order, without whitespace.
        var canonical = "{"
                      + string.Join(
                          ",",
                          jwk.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                             .Select(pair => $"\"{pair.Key}\":\"{pair.Value}\""))
                      + "}";
        var jkt = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));

        return (proof, jkt);
    }

    private static Mock<ITokenStore<SchemataToken>> NonceSlots() {
        var slots = new Mock<ITokenStore<SchemataToken>>();
        slots.Setup(value => value.GetOrCreateAsync(
                    null, "dpop", It.IsAny<string>(), null, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SchemataToken { Parent = null, Provider = "dpop", Key = "client-1", Value = ServerNonce });
        return slots;
    }

    private static ICacheProvider Cache() {
        var store = new Dictionary<string, byte[]>();
        var cache = new Mock<ICacheProvider>();
        cache.Setup(value => value.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((string key, CancellationToken _) =>
                 store.TryGetValue(key, out var bytes) ? bytes : null);
        cache.Setup(
                 value => value.TryAddAsync(
                     It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CacheEntryOptions>(),
                     It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, byte[] value, CacheEntryOptions _, CancellationToken _) =>
                store.TryAdd(key, value));
        return cache.Object;
    }
}
