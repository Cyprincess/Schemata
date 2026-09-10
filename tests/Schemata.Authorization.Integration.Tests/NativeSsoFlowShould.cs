using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Schemata.Abstractions.Advisors;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Integration.Tests.Fixtures;
using Schemata.Authorization.Skeleton.Advisors;
using Schemata.Authorization.Skeleton.Services;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Security.Skeleton;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Integration.Tests;

[Trait("Category", "Integration")]
[Trait("Layer", "Component")]
public class NativeSsoFlowShould
{
    private const string Issuer = "https://localhost";
    private const string Subject = "users/native-user";
    private const string Source = "native-source";
    private const string Target = "native-target";
    private const string Scope = "openid device_sso offline_access";
    private const string Salt = "native-regression-sector-salt";
    private const string Session = "native-session";

    [Theory]
    [InlineData(TokenFormats.Reference)]
    [InlineData(TokenFormats.Jwt)]
    [InlineData(TokenFormats.Jwe)]
    public async Task Exchange_Through_The_Canonical_Issuer_Produces_A_Usable_Bound_Token(string format) {
        using var factory = NewFactory(format);
        using var client = factory.CreateClient();
        await SeedClients(factory);
        var source = await IssueSource(factory, client);
        var response = await Exchange(client, source);
        var access = response.GetProperty("access_token").GetString()!;

        Assert.Equal(Schemes.Bearer, response.GetProperty("token_type").GetString());
        Assert.Equal(TokenTypeUris.AccessToken, response.GetProperty("issued_token_type").GetString());
        Assert.Equal("openid", response.GetProperty("scope").GetString());
        var id = new JsonWebToken(response.GetProperty("id_token").GetString()!);
        Assert.Equal(new[] { Target }, id.Audiences);
        Assert.Equal(Subject, id.Subject);
        Assert.False(response.TryGetProperty("device_secret", out _));
        Assert.Equal(format == TokenFormats.Reference ? 0 : format == TokenFormats.Jwt ? 2 : 4,
            access.Count(character => character == '.'));

        var introspection = await Introspect(client, access);
        Assert.True(introspection.GetProperty("active").GetBoolean());
        Assert.Equal(Subject, introspection.GetProperty("sub").GetString());
        Assert.Equal(Target, introspection.GetProperty("client_id").GetString());
        Assert.Equal("openid", introspection.GetProperty("scope").GetString());
        Assert.Equal(new[] { Issuer }, introspection.GetProperty("aud").EnumerateArray().Select(item => item.GetString()));
        using var bearer = new HttpRequestMessage(HttpMethod.Get, Endpoints.Profile);
        bearer.Headers.Authorization = new(Schemes.Bearer, access);
        var profile = await client.SendAsync(bearer);
        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
        var body = await Read(profile);
        Assert.Equal(Subject, body.GetProperty("sub").GetString());

        using var scope = factory.Services.CreateScope();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenStore<SchemataToken>>();
        var rows = new List<SchemataToken>();
        await foreach (var token in tokens.ListBySessionAsync(Session)) {
            if (token.Application == "applications/resource-" + Target && token.Type == TokenTypes.AccessToken) rows.Add(token);
        }
        var persisted = Assert.Single(rows);
        Assert.Equal(Subject, persisted.Parent);
        Assert.Equal(format, persisted.Format);
        if (format == TokenFormats.Reference) {
            Assert.Equal(access, persisted.ReferenceId);
            Assert.False(string.IsNullOrWhiteSpace(persisted.Payload));
        }
    }

    [Fact]
    public async Task Project_The_Canonical_Subject_Into_The_Target_Sector_And_Preserve_Storage_Ownership() {
        using var factory = NewFactory(TokenFormats.Reference);
        using var client = factory.CreateClient();
        await SeedClients(factory, pairwise: true);
        var source = await IssueSource(factory, client);
        var sourceSubject = new JsonWebToken(source.GetProperty("id_token").GetString()!).Subject;
        Assert.Equal(Pairwise("source.example"), sourceSubject);

        var issued = await Exchange(client, source);
        var access = issued.GetProperty("access_token").GetString()!;
        var introspection = await Introspect(client, access);
        Assert.True(introspection.GetProperty("active").GetBoolean());
        Assert.Equal(Pairwise("target.example"), introspection.GetProperty("sub").GetString());
        Assert.NotEqual(sourceSubject, introspection.GetProperty("sub").GetString());
        var id = new JsonWebToken(issued.GetProperty("id_token").GetString()!);
        Assert.Equal(new[] { Target }, id.Audiences);
        Assert.Equal(Pairwise("target.example"), id.Subject);
        using var scope = factory.Services.CreateScope();
        var token = await scope.ServiceProvider.GetRequiredService<ITokenStore<SchemataToken>>()
                               .FindByReferenceIdAsync(access);
        Assert.NotNull(token);
        Assert.Equal(Subject, token.Parent);
        Assert.Equal("applications/resource-" + Target, token.Application);
        Assert.Equal(Session, token.SessionId);
    }

    [Fact]
    public async Task Preserve_External_Resources_Across_Dispatch_And_Deny_The_Issuer_Resource() {
        using var factory = NewFactory(TokenFormats.Reference);
        using var client = factory.CreateClient();
        await SeedClients(factory);
        var source = await IssueSource(factory, client);
        var resources = new[] { "https://calendar.example/api", "https://contacts.example/api" };
        var issued = await Exchange(client, source, resources);
        var access = issued.GetProperty("access_token").GetString()!;
        var introspection = await Introspect(client, access);
        Assert.True(introspection.GetProperty("active").GetBoolean());
        Assert.Equal(resources, introspection.GetProperty("aud").EnumerateArray().Select(item => item.GetString()));
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoints.Profile);
        request.Headers.Authorization = new(Schemes.Bearer, access);
        var denied = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        Assert.Contains("invalid_token", denied.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task Apply_Extension_Claim_Destinations_To_The_Exchanged_Token() {
        using var factory = NewFactory(TokenFormats.Jwt, extension: true);
        using var client = factory.CreateClient();
        await SeedClients(factory);
        var source = await IssueSource(factory, client);
        var issued = await Exchange(client, source);
        var jwt = new JsonWebToken(issued.GetProperty("access_token").GetString()!);
        Assert.Equal("native-tenant", jwt.GetClaim("tenant").Value);
        Assert.False(jwt.TryGetClaim("private-note", out _));
        Assert.True((await Introspect(client, jwt.EncodedToken)).GetProperty("active").GetBoolean());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Refresh_Reuses_The_Code_Issued_Secret_Without_Another_Record(bool supplySecret) {
        using var factory = NewFactory(TokenFormats.Reference);
        using var client = factory.CreateClient();
        await SeedClients(factory);
        var source = await IssueSource(factory, client);
        var secret = source.GetProperty("device_secret").GetString()!;
        using (var scope = factory.Services.CreateScope()) {
            var token = await scope.ServiceProvider.GetRequiredService<ITokenStore<SchemataToken>>()
                                   .FindByReferenceIdAsync(secret);
            Assert.NotNull(token);
            Assert.Null(token.Parent);
            Assert.Equal("applications/resource-" + Source, token.Application);
            Assert.Equal(Session, token.SessionId);
        }
        var before = await SecretCount(factory);
        var refreshed = await Refresh(client, source, supplySecret ? secret : null);
        Assert.Equal(secret, refreshed.GetProperty("device_secret").GetString());
        AssertDeviceSecretBinding(refreshed);
        Assert.Equal(before, await SecretCount(factory));
        Assert.True((await Introspect(client, refreshed.GetProperty("access_token").GetString()!))
            .GetProperty("active").GetBoolean());
    }

    [Theory]
    [InlineData("client")]
    [InlineData("session")]
    [InlineData("device")]
    [InlineData("expired")]
    [InlineData("revoked")]
    public async Task Refresh_Does_Not_Reuse_A_Secret_Outside_Its_Live_Binding(string mismatch) {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var factory = NewFactory(TokenFormats.Reference, clock: clock);
        using var client = factory.CreateClient();
        await SeedClients(factory);
        var source = await IssueSource(factory, client);
        var secret = source.GetProperty("device_secret").GetString()!;
        using (var scope = factory.Services.CreateScope()) {
            var tokens = scope.ServiceProvider.GetRequiredService<ITokenStore<SchemataToken>>();
            var token = await tokens.FindByReferenceIdAsync(secret);
            Assert.NotNull(token);
            switch (mismatch) {
                case "client": token.Application = "applications/resource-" + Target; break;
                case "session": token.SessionId = "other-session"; break;
                case "device": token.DeviceId = "other-device"; break;
                case "expired": token.ExpireTime = clock.GetUtcNow().UtcDateTime; break;
                case "revoked": token.Status = TokenStatuses.Revoked; break;
            }
            await tokens.UpdateAsync(token);
        }
        var before = await SecretCount(factory);
        var refreshed = await Refresh(client, source);
        var replacement = refreshed.GetProperty("device_secret").GetString()!;
        Assert.NotEqual(secret, replacement);
        AssertDeviceSecretBinding(refreshed);
        Assert.Equal(before + 1, await SecretCount(factory));
        using var verify = factory.Services.CreateScope();
        var created = await verify.ServiceProvider.GetRequiredService<ITokenStore<SchemataToken>>()
                                  .FindByReferenceIdAsync(replacement);
        Assert.NotNull(created);
        Assert.Equal("applications/resource-" + Source, created.Application);
        Assert.Equal(Session, created.SessionId);
        Assert.Equal("native-device", created.DeviceId);
    }

    private static WebAppFactory NewFactory(string format, bool extension = false, FakeTimeProvider? clock = null) {
        return new WebAppFactory().WithEnvironment("Native").WithServices(services => {
            services.PostConfigure<SchemataAuthorizationOptions>(options => {
                options.AccessTokenFormat = format;
                options.PairwiseSalt = Salt;
            });
            services.AddSingleton<TimeProvider>(clock ?? new FakeTimeProvider(DateTimeOffset.UtcNow));
            services.AddSingleton<IDeviceIdResolver, NativeDeviceResolver>();
            if (extension) {
                services.AddSingleton<IClaimsAdvisor, TenantClaims>();
                services.AddSingleton<IDestinationAdvisor, TenantDestination>();
            }
        });
    }

    private static async Task SeedClients(WebAppFactory factory, bool pairwise = false) {
        using var scope = factory.Services.CreateScope();
        var apps = scope.ServiceProvider.GetRequiredService<IApplicationManager<SchemataApplication>>();
        var securities = scope.ServiceProvider.GetRequiredService<ISecurityStore<SchemataSecurity>>();
        var verifier = scope.ServiceProvider.GetRequiredService<ISecretVerifier>();
        foreach (var clientId in new[] { Source, Target }) {
            var app = new SchemataApplication {
                Name = "resource-" + clientId,
                ClientId = clientId,
                ClientType = ClientTypes.Confidential,
                RedirectUris = [Issuer + "/callback"],
                SubjectType = pairwise ? SubjectTypes.Pairwise : SubjectTypes.Public,
                SectorIdentifierUri = "https://" + (clientId == Source ? "source" : "target") + ".example/sector.json",
                Permissions = ["e:/Connect/Token", "g:authorization_code", "g:refresh_token",
                    "g:" + GrantTypes.TokenExchange, "s:openid", "s:device_sso", "s:offline_access"],
            };
            await apps.CreateAsync(app);
            await securities.CreateAsync(new() {
                Parent = SecurityParents.Application(app),
                Key = clientId,
                Kind = SecurityConstants.Kinds.Password,
                Usage = SecurityConstants.Usages.Authentication,
                Algorithm = SecurityConstants.Algorithms.Pbkdf2,
                Value = await verifier.HashAsync("native-secret"),
                Status = SecurityConstants.Statuses.Valid,
            });
        }
    }

    private static async Task<JsonElement> IssueSource(WebAppFactory factory, HttpClient client) {
        using var scope = factory.Services.CreateScope();
        var signIn = scope.ServiceProvider.GetRequiredService<IAuthorizationSignInService>();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new(IdentityClaims.Subject, Subject), new(Claims.ClientId, Source),
        ], "test"));
        var code = await signIn.IssueAsync(principal, new Dictionary<string, string?> {
            [Properties.GrantType] = GrantTypes.AuthorizationCode,
            [Properties.ResponseType] = ResponseTypes.Code,
            [Properties.Scope] = Scope,
            [Properties.RedirectUri] = Issuer + "/callback",
            [Properties.SessionId] = Session,
        }, AuthorizationSignInResponseKind.Callback);
        Assert.NotNull(code.Callback);
        var source = await Token(client, [
            new("grant_type", GrantTypes.AuthorizationCode), new("client_id", Source),
            new("client_secret", "native-secret"), new("redirect_uri", Issuer + "/callback"),
            new("code", code.Callback.Parameters[Parameters.Code]!),
        ]);
        AssertDeviceSecretBinding(source);
        return source;
    }

    private static void AssertDeviceSecretBinding(JsonElement response) {
        var secret = response.GetProperty("device_secret").GetString()!;
        var id = new JsonWebToken(response.GetProperty("id_token").GetString()!);
        Assert.Equal(SigningAlgorithms.RsaSha256, id.Alg);
        var digest = SHA256.HashData(Encoding.ASCII.GetBytes(secret));
        Assert.Equal(Base64UrlEncoder.Encode(digest.AsSpan(0, digest.Length / 2).ToArray()),
            id.GetClaim(Claims.DsHash).Value);
        Assert.Equal(Session, id.GetClaim(Claims.SessionId).Value);
        Assert.Equal(new[] { Source }, id.Audiences);
    }

    private static Task<JsonElement> Exchange(HttpClient client, JsonElement source, params string[] resources) {
        var fields = new List<KeyValuePair<string, string>> {
            new("grant_type", GrantTypes.TokenExchange), new("client_id", Target),
            new("client_secret", "native-secret"), new("subject_token", source.GetProperty("id_token").GetString()!),
            new("subject_token_type", TokenTypeUris.IdToken),
            new("actor_token", source.GetProperty("device_secret").GetString()!),
            new("actor_token_type", TokenTypeUris.DeviceSecret),
            new("requested_token_type", TokenTypeUris.AccessToken), new("audience", Issuer), new("scope", "openid"),
        };
        fields.AddRange(resources.Select(resource => new KeyValuePair<string, string>("resource", resource)));
        return Token(client, fields);
    }

    private static Task<JsonElement> Refresh(HttpClient client, JsonElement source, string? secret = null) {
        var fields = new List<KeyValuePair<string, string>> {
            new("grant_type", GrantTypes.RefreshToken), new("client_id", Source),
            new("client_secret", "native-secret"), new("refresh_token", source.GetProperty("refresh_token").GetString()!),
        };
        if (secret is not null) fields.Add(new("device_secret", secret));
        return Token(client, fields);
    }

    private static async Task<JsonElement> Token(HttpClient client, List<KeyValuePair<string, string>> fields) {
        using var response = await client.PostAsync(Endpoints.Token, new FormUrlEncodedContent(fields));
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await Read(response);
    }

    private static async Task<JsonElement> Introspect(HttpClient client, string access) {
        using var response = await client.PostAsync("/Connect/Introspect", new FormUrlEncodedContent([
            new("client_id", "introspect-client"), new("client_secret", "introspect-secret"), new("token", access),
        ]));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await Read(response);
    }

    private static async Task<JsonElement> Read(HttpResponseMessage response) {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        return document.RootElement.Clone();
    }

    private static async Task<int> SecretCount(WebAppFactory factory) {
        using var scope = factory.Services.CreateScope();
        var contexts = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AuthorizationDbContext>>();
        await using var context = await contexts.CreateDbContextAsync();
        return await context.Set<SchemataToken>().CountAsync(token => token.Type == TokenTypes.DeviceSecret);
    }

    private static string Pairwise(string sector) =>
        Base64UrlEncoder.Encode(SHA256.HashData(Encoding.UTF8.GetBytes(sector + Subject + Salt)));

    private sealed class NativeDeviceResolver : IDeviceIdResolver
    {
        public Task<string?> ResolveAsync(ClaimsPrincipal? principal, string? subject, CancellationToken ct = default) =>
            Task.FromResult<string?>("native-device");
    }

    private sealed class TenantClaims : IClaimsAdvisor
    {
        public int Order => 0;
        public Task<AdviseResult> AdviseAsync(AdviceContext ctx, List<Claim> claims, CancellationToken ct = default) {
            claims.Add(new("tenant", "native-tenant"));
            claims.Add(new("private-note", "internal"));
            return Task.FromResult(AdviseResult.Continue);
        }
    }

    private sealed class TenantDestination : IDestinationAdvisor
    {
        public int Order => 0;
        public Task<AdviseResult> AdviseAsync(AdviceContext ctx, Claim claim, HashSet<string> destinations,
            ClaimsPrincipal principal, CancellationToken ct = default) {
            if (claim.Type == "tenant") destinations.Add(ClaimDestinations.AccessToken);
            return Task.FromResult(AdviseResult.Continue);
        }
    }
}
