using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Integration.Tests.Fixtures;
using Schemata.Authorization.Skeleton;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Services;
using Schemata.Security.Foundation;
using Schemata.Security.Skeleton;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using Xunit;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Integration.Tests;

[Trait("Category", "Integration")]
[Trait("Layer", "Component")]
public class EncryptedIdTokenFlowShould
{
    [Fact]
    public async Task Encrypt_Token_Response_And_Callback_After_All_Inner_Claims_Are_Complete() {
        using var factory = new WebAppFactory();
        using var clientKey = RSA.Create(2048);
        await SeedAsync(factory, clientKey);
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var signIn = services.GetRequiredService<IAuthorizationSignInService>();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("sub", "users/alice"), new Claim("sid", "sid-a"), new Claim("client_id", "encrypted-client"),
        ], "host"));
        var properties = new Dictionary<string, string?> {
            [Properties.GrantType] = GrantTypes.AuthorizationCode,
            [Properties.Scope] = Scopes.OpenId,
            [Properties.SessionId] = "sid-a",
            [Properties.GrantProfile] = GrantProfiles.OpenIdConnect,
            [Properties.Nonce] = "nonce-1",
        };

        var token = await signIn.IssueAsync(principal, properties, AuthorizationSignInResponseKind.Token);
        properties[Properties.ResponseType] = "code id_token token";
        properties[Properties.RedirectUri] = "https://rp.example/cb";
        var callback = await signIn.IssueAsync(principal, properties, AuthorizationSignInResponseKind.Callback);

        var tokenClaims = await DecryptAndValidateAsync(services, clientKey, token.Token!.IdToken!);
        Assert.Equal("nonce-1", tokenClaims.FindFirst(Claims.Nonce)?.Value);
        AssertHash(token.Token.AccessToken!, tokenClaims.FindFirst(Claims.AtHash)?.Value);
        var callbackParameters = callback.Callback!.Parameters;
        var callbackClaims = await DecryptAndValidateAsync(services, clientKey, callbackParameters[Parameters.IdToken]!);
        Assert.Equal("nonce-1", callbackClaims.FindFirst(Claims.Nonce)?.Value);
        AssertHash(callbackParameters[Parameters.AccessToken]!, callbackClaims.FindFirst(Claims.AtHash)?.Value);
        AssertHash(callbackParameters[Parameters.Code]!, callbackClaims.FindFirst("c_hash")?.Value);

        using var discovery = await JsonDocument.ParseAsync(
            await factory.CreateClient().GetStreamAsync("/.well-known/openid-configuration"));
        Assert.Equal([EncryptionAlgorithms.RsaOaep],
            discovery.RootElement.GetProperty("id_token_encryption_alg_values_supported").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal([
                ContentEncryptionAlgorithms.Aes128CbcHmacSha256,
                ContentEncryptionAlgorithms.Aes192CbcHmacSha384,
                ContentEncryptionAlgorithms.Aes256CbcHmacSha512,
            ],
            discovery.RootElement.GetProperty("id_token_encryption_enc_values_supported").EnumerateArray().Select(value => value.GetString()));
    }

    private static async Task<ClaimsPrincipal> DecryptAndValidateAsync(
        IServiceProvider services,
        RSA clientKey,
        string encrypted
    ) {
        var outer = new JsonWebToken(encrypted);
        Assert.Equal("JWT", outer.Cty);
        var inner = new JsonWebTokenHandler().DecryptToken(outer, new TokenValidationParameters {
            TokenDecryptionKey = new RsaSecurityKey(clientKey),
        });
        Assert.Equal(SigningAlgorithms.RsaSha256, new JsonWebToken(inner).Alg);
        return await services.GetRequiredService<TokenService<SchemataSecurity>>().Validate(inner)
            ?? throw new InvalidOperationException("The inner ID token did not validate.");
    }

    private static void AssertHash(string value, string? hash) {
        Assert.False(string.IsNullOrWhiteSpace(hash));
        var digest = SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(value));
        Assert.Equal(Base64UrlEncoder.Encode(digest.AsSpan(0, digest.Length / 2).ToArray()), hash);
    }

    private static async Task SeedAsync(WebAppFactory factory, RSA clientKey) {
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var application = new SchemataApplication {
            Name = "encrypted-client", ClientId = "encrypted-client", TokenEndpointAuthMethod = ClientAuthMethods.None,
            GrantTypes = [GrantTypes.AuthorizationCode, GrantTypes.Implicit],
            ResponseTypes = [ResponseTypes.Code, "code id_token token"], Scope = Scopes.OpenId,
            RedirectUris = ["https://rp.example/cb"], IdTokenEncryptedResponseAlg = EncryptionAlgorithms.RsaOaep,
        };
        await services.GetRequiredService<IApplicationManager<SchemataApplication>>().CreateAsync(application);
        var key = clientKey.ExportParameters(false);
        await services.GetRequiredService<ISecurityStore<SchemataSecurity>>().CreateAsync(new() {
            Parent = SecurityParents.Application(application), Key = "encrypted-id-client",
            Kind = SecurityConstants.Kinds.Jwks, Usage = SecurityConstants.Usages.Authentication,
            Status = SecurityConstants.Statuses.Valid,
            Value = JsonSerializer.Serialize(new {
                keys = new[] {
                    new { kty = "RSA", kid = "encrypted-id-client", use = "enc", alg = EncryptionAlgorithms.RsaOaep,
                        n = Base64UrlEncoder.Encode(key.Modulus!), e = Base64UrlEncoder.Encode(key.Exponent!) },
                },
            }),
        });
    }
}
