using System;
using System.Collections.Generic;
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
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Integration.Tests.Fixtures;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Services;
using Schemata.Security.Skeleton;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Integration.Tests;

/// <summary>
///     Regression host for the authorization-code <c>invalid_grant</c> defect: Native SSO
///     device-secret issuance on a host that never enables Session Management. The session
///     identifier is minted at sign-in issuance and must bind the whole token chain without a
///     browser channel, per OpenID Connect Native SSO 1.0 §3.4.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Layer", "Component")]
public class NativeSsoNoSessionHostShould
{
    private const string Issuer  = "https://localhost";
    private const string Subject = "users/native-user";
    private const string Source  = "native-source";
    private const string Scope   = "openid device_sso offline_access";

    [Fact]
    public async Task Redeem_A_Fresh_Code_And_Bind_The_Device_Secret_To_The_Minted_Session() {
        using var factory = NewFactory();
        using var client  = factory.CreateClient();
        await SeedClient(factory);

        var source  = await IssueCodeAndExchange(client, factory);
        var sid     = SidOf(source);
        var secret  = source.GetProperty("device_secret").GetString()!;
        var id      = new JsonWebToken(source.GetProperty("id_token").GetString()!);
        var digest  = SHA256.HashData(Encoding.ASCII.GetBytes(secret));

        Assert.Equal(Base64UrlEncoder.Encode(digest.AsSpan(0, digest.Length / 2).ToArray()),
            id.GetClaim(Claims.DsHash).Value);

        using (var scope = factory.Services.CreateScope()) {
            var tokens = scope.ServiceProvider.GetRequiredService<ITokenStore<SchemataToken>>();
            var device = await tokens.FindByReferenceIdAsync(secret);
            Assert.NotNull(device);
            Assert.Equal(sid, device.SessionId);
            Assert.Equal("native-device", device.DeviceId);
        }
    }

    [Fact]
    public async Task Refresh_Along_The_Minted_Session_Without_Reissuing_A_Device_Secret() {
        using var factory = NewFactory();
        using var client  = factory.CreateClient();
        await SeedClient(factory);

        var source = await IssueCodeAndExchange(client, factory);
        var sid    = SidOf(source);
        var before = await SecretCount(factory);

        var fields = new List<KeyValuePair<string, string>> {
            new("grant_type", GrantTypes.RefreshToken), new("client_id", Source),
            new("client_secret", "native-secret"),
            new("refresh_token", source.GetProperty("refresh_token").GetString()!),
        };
        JsonElement refreshed;
        using (var response = await client.PostAsync(Endpoints.Token, new FormUrlEncodedContent(fields))) {
            Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
            refreshed = document.RootElement.Clone();
        }

        Assert.Equal(source.GetProperty("device_secret").GetString(), refreshed.GetProperty("device_secret").GetString());
        Assert.Equal(sid, SidOf(refreshed));
        Assert.Equal(before, await SecretCount(factory));
    }

    private static WebAppFactory NewFactory() {
        return new WebAppFactory().WithEnvironment("NoSession").WithServices(services => {
            services.PostConfigure<SchemataAuthorizationOptions>(options => {
                options.AccessTokenFormat = TokenFormats.Reference;
            });
            services.AddSingleton<TimeProvider>(new FakeTimeProvider(DateTimeOffset.UtcNow));
            services.AddSingleton<IDeviceIdResolver, NativeDeviceResolver>();
        });
    }

    private static async Task SeedClient(WebAppFactory factory) {
        using var scope = factory.Services.CreateScope();
        var apps      = scope.ServiceProvider.GetRequiredService<IApplicationManager<SchemataApplication>>();
        var securities = scope.ServiceProvider.GetRequiredService<ISecurityStore<SchemataSecurity>>();
        var verifier  = scope.ServiceProvider.GetRequiredService<ISecretVerifier>();
        var app = new SchemataApplication {
            Name         = "resource-" + Source,
            ClientId     = Source,
            TokenEndpointAuthMethod = ClientAuthMethods.ClientSecretPost,
            RedirectUris = [Issuer + "/callback"],
            Permissions  = ["e:/Connect/Token"],
            GrantTypes   = ["authorization_code", "refresh_token"],
            ResponseTypes = ["code"],
            Scope        = "openid device_sso offline_access",
        };
        await apps.CreateAsync(app);
        await securities.CreateAsync(new() {
            Parent    = SecurityParents.Application(app),
            Key       = Source,
            Kind      = SecurityConstants.Kinds.Password,
            Usage     = SecurityConstants.Usages.Authentication,
            Algorithm = SecurityConstants.Algorithms.Pbkdf2,
            Value     = await verifier.HashAsync("native-secret"),
            Status    = SecurityConstants.Statuses.Valid,
        });
    }

    private static async Task<JsonElement> IssueCodeAndExchange(HttpClient client, WebAppFactory factory) {
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
            [Properties.GrantProfile] = GrantProfiles.OpenIdConnect,
        }, AuthorizationSignInResponseKind.Callback);
        Assert.NotNull(code.Callback);

        var fields = new List<KeyValuePair<string, string>> {
            new("grant_type", GrantTypes.AuthorizationCode), new("client_id", Source),
            new("client_secret", "native-secret"), new("redirect_uri", Issuer + "/callback"),
            new("code", code.Callback.Parameters[Parameters.Code]!),
        };
        using var response = await client.PostAsync(Endpoints.Token, new FormUrlEncodedContent(fields));
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        return document.RootElement.Clone();
    }

    private static string SidOf(JsonElement response) {
        var sid = new JsonWebToken(response.GetProperty("id_token").GetString()!).GetClaim(Claims.SessionId).Value;
        Assert.False(string.IsNullOrWhiteSpace(sid));
        return sid;
    }

    private static async Task<int> SecretCount(WebAppFactory factory) {
        using var scope = factory.Services.CreateScope();
        var contexts = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AuthorizationDbContext>>();
        await using var context = await contexts.CreateDbContextAsync();
        return await context.Set<SchemataToken>().CountAsync(token => token.Type == TokenTypes.DeviceSecret);
    }

    private sealed class NativeDeviceResolver : IDeviceIdResolver
    {
        public Task<string?> ResolveAsync(ClaimsPrincipal? principal, string? subject, CancellationToken ct = default) =>
            Task.FromResult<string?>("native-device");
    }
}
