using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
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
public class JarLifetimeShould
{
    [Fact]
    public async Task Reject_An_Expired_Request_Object_When_Its_Pushed_Request_Uri_Is_Still_Valid() {
        const string clientId = "code-client";
        const string keyId = "jar-signing-key";
        var anchor = new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(anchor);
        using var rsa = RSA.Create(2048);
        using var factory = new WebAppFactory().WithEnvironment("Jar").WithServices(services => {
            services.AddSingleton<TimeProvider>(clock);
            services.Configure<JwtSecuredAuthorizationRequestsOptions>(options =>
                options.SigningAlgorithms.Add(SecurityAlgorithms.RsaSha256));
            services.Configure<PushedAuthorizationRequestsOptions>(options =>
                options.Lifetime = TimeSpan.FromMinutes(10));
        });
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        using (var scope = factory.Services.CreateScope()) {
            var applications = scope.ServiceProvider.GetRequiredService<IApplicationManager<SchemataApplication>>();
            var application = await applications.FindByClientIdAsync(clientId);
            Assert.NotNull(application);
            application.RequestObjectSigningAlg = SecurityAlgorithms.RsaSha256;
            await applications.UpdateAsync(application);

            var parameters = rsa.ExportParameters(false);
            var securities = scope.ServiceProvider.GetRequiredService<ISecurityStore<SchemataSecurity>>();
            await securities.CreateAsync(new() {
                Parent = SecurityParents.Application(application),
                Name = keyId,
                Kind = SecurityConstants.Kinds.Jwks,
                Usage = SecurityConstants.Usages.Authentication,
                Status = SecurityConstants.Statuses.Valid,
                Value = JsonSerializer.Serialize(new {
                    keys = new[] {
                        new {
                            kty = "RSA",
                            kid = keyId,
                            use = "sig",
                            n = Base64UrlEncoder.Encode(parameters.Modulus!),
                            e = Base64UrlEncoder.Encode(parameters.Exponent!),
                        },
                    },
                }),
            });
        }

        var assertion = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor {
            Issuer = clientId,
            Audience = "https://localhost",
            IssuedAt = anchor.UtcDateTime,
            NotBefore = anchor.UtcDateTime,
            Expires = anchor.AddMinutes(1).UtcDateTime,
            Claims = new Dictionary<string, object> {
                [Parameters.ClientId] = clientId,
                [Parameters.RedirectUri] = "https://localhost/callback",
                [Parameters.ResponseType] = ResponseTypes.Code,
                [Parameters.Scope] = Scopes.OpenId,
                [Parameters.State] = "jar-lifetime",
                [Parameters.CodeChallenge] = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM",
                [Parameters.CodeChallengeMethod] = "S256",
            },
            SigningCredentials = new(new RsaSecurityKey(rsa) { KeyId = keyId }, SecurityAlgorithms.RsaSha256),
        });
        using var form = new FormUrlEncodedContent(new Dictionary<string, string> {
            [Parameters.ClientId] = clientId,
            [Parameters.ClientSecret] = "code-secret",
            [Parameters.Request] = assertion,
        });
        using var pushed = await client.PostAsync("/Connect/Par", form);
        Assert.True(pushed.StatusCode == HttpStatusCode.Created,
            $"{(int)pushed.StatusCode}: {await pushed.Content.ReadAsStringAsync()}");
        using var payload = await JsonDocument.ParseAsync(await pushed.Content.ReadAsStreamAsync());
        var requestUri = payload.RootElement.GetProperty("request_uri").GetString();
        Assert.NotNull(requestUri);
        Assert.StartsWith(RequestUriPrefixes.Par, requestUri);
        Assert.True(payload.RootElement.GetProperty("expires_in").GetInt32() > TimeSpan.FromMinutes(3).TotalSeconds);

        clock.Advance(TimeSpan.FromMinutes(3));
        using (var scope = factory.Services.CreateScope()) {
            var tokens = scope.ServiceProvider.GetRequiredService<ITokenStore<SchemataToken>>();
            var stored = await tokens.FindByReferenceIdAsync(requestUri[RequestUriPrefixes.Par.Length..]);
            Assert.NotNull(stored);
            Assert.Equal(TokenTypes.ParRequest, stored.Type);
            Assert.Equal(TokenStatuses.Valid, stored.Status);
            Assert.True(stored.ExpireTime > clock.GetUtcNow().UtcDateTime);
        }

        using var response = await client.GetAsync("/Connect/Authorize?client_id=" + clientId
            + "&request_uri=" + Uri.EscapeDataString(requestUri));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var error = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal(OAuthErrors.InvalidRequestObject, error.RootElement.GetProperty("error").GetString());
    }
}
