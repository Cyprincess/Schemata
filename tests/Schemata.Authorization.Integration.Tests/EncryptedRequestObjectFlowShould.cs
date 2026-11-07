using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Integration.Tests.Fixtures;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Security.Skeleton;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using Xunit;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Integration.Tests;

[Trait("Category", "Integration")]
[Trait("Layer", "Component")]
public sealed class EncryptedRequestObjectFlowShould
{
    [Fact]
    public async Task Redeem_An_Encrypted_Par_Object_And_Advertise_The_Negotiated_Algorithms() {
        using var clientKey = RSA.Create(2048);
        await using var factory = new WebAppFactory().WithEnvironment("Jar").WithServices(services => {
            services.Configure<JwtSecuredAuthorizationRequestsOptions>(options => {
                options.SigningAlgorithms.Add(SigningAlgorithms.RsaSha256);
                options.EncryptionAlgorithms.Add(EncryptionAlgorithms.RsaOaep);
                options.ContentEncryptionAlgorithms.Add(ContentEncryptionAlgorithms.Aes256CbcHmacSha512);
            });
        });
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        using var issuerKey = await SeedRegistration(factory, clientKey);
        var assertion = Mint(clientKey, issuerKey);

        using var form = new FormUrlEncodedContent(new Dictionary<string, string> {
            [Parameters.ClientId] = "code-client",
            [Parameters.ClientSecret] = "code-secret",
            [Parameters.Request] = assertion,
        });
        using var pushed = await client.PostAsync(Endpoints.Par, form);
        Assert.Equal(HttpStatusCode.Created, pushed.StatusCode);
        using var par = await JsonDocument.ParseAsync(await pushed.Content.ReadAsStreamAsync());
        var requestUri = par.RootElement.GetProperty("request_uri").GetString();
        Assert.NotNull(requestUri);

        using var authorize = await client.GetAsync(Endpoints.Authorize
            + "?client_id=code-client&request_uri=" + Uri.EscapeDataString(requestUri));
        Assert.Equal(HttpStatusCode.Found, authorize.StatusCode);
        Assert.NotNull(authorize.Headers.Location);
        Assert.Equal("https://localhost/interact", authorize.Headers.Location.GetLeftPart(UriPartial.Path));

        using var discovery = await JsonDocument.ParseAsync(
            await client.GetStreamAsync("/.well-known/openid-configuration"));
        Assert.Equal(EncryptionAlgorithms.RsaOaep,
            discovery.RootElement.GetProperty("request_object_encryption_alg_values_supported")[0].GetString());
        Assert.Equal(ContentEncryptionAlgorithms.Aes256CbcHmacSha512,
            discovery.RootElement.GetProperty("request_object_encryption_enc_values_supported")[0].GetString());
    }

    private static async Task<RSA> SeedRegistration(WebAppFactory factory, RSA clientKey) {
        await using var scope = factory.Services.CreateAsyncScope();
        var applications = scope.ServiceProvider.GetRequiredService<IApplicationManager<SchemataApplication>>();
        var application = await applications.FindByClientIdAsync("code-client");
        Assert.NotNull(application);
        application.RequestObjectSigningAlg = SigningAlgorithms.RsaSha256;
        application.RequestObjectEncryptionAlg = EncryptionAlgorithms.RsaOaep;
        application.RequestObjectEncryptionEnc = ContentEncryptionAlgorithms.Aes256CbcHmacSha512;
        await applications.UpdateAsync(application);

        var clientParameters = clientKey.ExportParameters(false);
        var securities = scope.ServiceProvider.GetRequiredService<ISecurityStore<SchemataSecurity>>();
        await securities.CreateAsync(new() {
            Parent = SecurityParents.Application(application),
            Key = "encrypted-jar-client",
            Kind = SecurityConstants.Kinds.Jwks,
            Usage = SecurityConstants.Usages.Authentication,
            Status = SecurityConstants.Statuses.Valid,
            Value = JsonSerializer.Serialize(new {
                keys = new[] {
                    new {
                        kty = "RSA",
                        kid = "encrypted-jar-client",
                        use = "sig",
                        n = Base64UrlEncoder.Encode(clientParameters.Modulus!),
                        e = Base64UrlEncoder.Encode(clientParameters.Exponent!),
                    },
                },
            }),
        });

        SchemataSecurity? issuerRow = null;
        await foreach (var row in securities.ListByParentAsync(
                           SecurityParents.Issuer("https://localhost"),
                           usage: SecurityConstants.Usages.Encryption,
                           status: SecurityConstants.Statuses.Valid)) {
            issuerRow = row;
            break;
        }
        Assert.NotNull(issuerRow?.Value);
        var issuerKey = RSA.Create();
        issuerKey.ImportFromPem(issuerRow.Value);
        return issuerKey;
    }

    private static string Mint(RSA clientKey, RSA issuerKey) {
        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(new SecurityTokenDescriptor {
            Issuer = "code-client",
            Audience = "https://localhost",
            Claims = new Dictionary<string, object> {
                [Parameters.ClientId] = "code-client",
                [Parameters.RedirectUri] = "https://localhost/callback",
                [Parameters.ResponseType] = ResponseTypes.Code,
                [Parameters.Scope] = Scopes.OpenId,
                [Parameters.State] = "encrypted-par",
                [Parameters.CodeChallenge] = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM",
                [Parameters.CodeChallengeMethod] = "S256",
            },
            SigningCredentials = new(
                new RsaSecurityKey(clientKey) { KeyId = "encrypted-jar-client" },
                SigningAlgorithms.RsaSha256),
            EncryptingCredentials = new(
                new RsaSecurityKey(issuerKey),
                EncryptionAlgorithms.RsaOaep,
                ContentEncryptionAlgorithms.Aes256CbcHmacSha512),
            AdditionalHeaderClaims = new Dictionary<string, object> { ["cty"] = "JWT" },
        });
    }
}
