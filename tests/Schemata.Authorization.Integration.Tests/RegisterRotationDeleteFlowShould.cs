using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Authorization.Foundation.Authentication;
using Schemata.Authorization.Integration.Tests.Fixtures;
using Schemata.Security.Skeleton;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using Xunit;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Integration.Tests;

/// <summary>
///     RFC 7592 registration management over real HTTP: opt-in rotation returns only a usable
///     successor while retiring the predecessor, and a delete leaves no application authority,
///     live registration token, client credential, or application grant behind.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Layer", "Component")]
public class RegisterRotationDeleteFlowShould(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    [Fact]
    public async Task Rotate_On_Read_Returns_A_Usable_Successor_And_Retires_The_Predecessor() {
        using var host = factory.WithEnvironment("Testing")
                                .WithServices(services => services.PostConfigure<SchemataAuthorizationOptions>(
                                                 options => options.RotateRegistrationTokens = true));
        using var client = host.CreateClient();

        var (clientId, rat1, _) = await RegisterAsync(client, new() {
            ["redirect_uris"] = new[] { "https://rp.example/cb" },
        });

        var (status, body, _) = await ReadAsync(client, clientId, rat1);
        Assert.Equal(HttpStatusCode.OK, status);
        var rat2 = JsonDocument.Parse(body).RootElement.GetProperty("registration_access_token").GetString();
        Assert.NotNull(rat2);
        Assert.NotEqual(rat1, rat2);

        // The retired predecessor is rejected.
        var (retiredStatus, _, retiredChallenge) = await ReadAsync(client, clientId, rat1);
        Assert.Equal(HttpStatusCode.Unauthorized, retiredStatus);
        Assert.Equal("Bearer error=\"invalid_token\"", retiredChallenge);

        // The successor is usable and rotates again on use.
        var (successorStatus, successorBody, _) = await ReadAsync(client, clientId, rat2);
        Assert.Equal(HttpStatusCode.OK, successorStatus);
        var rat3 = JsonDocument.Parse(successorBody).RootElement.GetProperty("registration_access_token").GetString();
        Assert.NotNull(rat3);
        Assert.NotEqual(rat2, rat3);

        // Exactly one live registration token remains; a null lifetime stays non-expiring on
        // every successor.
        using var scope = host.Services.CreateScope();
        var contexts    = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AuthorizationDbContext>>();
        await using var context = await contexts.CreateDbContextAsync();
        var canonical = await context.Applications
                                     .Where(a => a.ClientId == clientId)
                                     .Select(a => a.CanonicalName)
                                     .SingleAsync();
        var registrations = await context.Tokens
                                         .Where(t => t.Application == canonical && t.Type == TokenTypes.Registration)
                                         .ToListAsync();

        var live = Assert.Single(registrations, t => t.Status == TokenStatuses.Valid);
        Assert.Equal(rat3, live.ReferenceId);
        Assert.Null(live.ExpireTime);
        Assert.Equal(2, registrations.Count(t => t.Status == TokenStatuses.Redeemed));
    }

    [Fact]
    public async Task Delete_Removes_The_Application_And_All_Canonical_Dependents() {
        using var host   = factory.WithEnvironment("Testing");
        using var client = host.CreateClient();

        var (clientId, rat, created) = await RegisterAsync(client, new() {
            ["grant_types"]                 = new[] { GrantTypes.ClientCredentials },
            ["token_endpoint_auth_method"]  = ClientAuthMethods.ClientSecretPost,
            ["jwks"] = JsonDocument.Parse("""{"keys":[{"kty":"RSA","kid":"rp-1","use":"sig","n":"x","e":"AQAB"}]}""").RootElement,
        });
        var secret = created.GetProperty("client_secret").GetString();
        Assert.NotNull(secret);

        string canonical;
        using (var scope = host.Services.CreateScope()) {
            var contexts = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AuthorizationDbContext>>();
            await using var context = await contexts.CreateDbContextAsync();
            canonical = (await context.Applications
                                      .Where(a => a.ClientId == clientId)
                                      .Select(a => a.CanonicalName)
                                      .SingleAsync())!;

            // Participation is associated with the application and is removed with its credentials.
            context.Subjects.Add(new() {
                Uid           = Guid.NewGuid(),
                Name          = "u-1",
                CanonicalName = "users/u-1",
            });
            await context.SaveChangesAsync();

            var tokens = scope.ServiceProvider.GetRequiredService<ITokenStore<SchemataToken>>();
            await tokens.CreateAsync(new() {
                Application = canonical,
                Type        = TokenTypes.AccessToken,
                Status      = TokenStatuses.Valid,
                Format      = TokenFormats.Reference,
                ReferenceId = $"access-{Guid.NewGuid():n}",
                ExpireTime  = DateTime.UtcNow.AddHours(1),
            });
            await tokens.CreateAsync(new() {
                Application = canonical,
                Type        = TokenTypes.RefreshToken,
                Status      = TokenStatuses.Valid,
                Format      = TokenFormats.Reference,
                ReferenceId = $"refresh-{Guid.NewGuid():n}",
                ExpireTime  = DateTime.UtcNow.AddDays(1),
            });
            await tokens.RegisterParticipantAsync("users/u-1", "session-1", canonical);
        }

        var (deleted, _) = await DeleteAsync(client, clientId, rat);
        Assert.Equal(HttpStatusCode.NoContent, deleted);

        // The management token is dead with the application.
        var (readStatus, _, readChallenge) = await ReadAsync(client, clientId, rat);
        Assert.Equal(HttpStatusCode.Unauthorized, readStatus);
        Assert.Equal("Bearer error=\"invalid_token\"", readChallenge);

        // The client secret no longer authenticates: the client lookup itself is gone.
        using var grant = new FormUrlEncodedContent(new Dictionary<string, string> {
            ["grant_type"] = GrantTypes.ClientCredentials, ["client_id"] = clientId, ["client_secret"] = secret,
        });
        var tokenResponse = await client.PostAsync("/connect/token", grant);
        Assert.Equal(HttpStatusCode.BadRequest, tokenResponse.StatusCode);
        var error = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(OAuthErrors.InvalidClient, error.GetProperty("error").GetString());

        using var verifyScope = host.Services.CreateScope();
        var verifyContexts    = verifyScope.ServiceProvider.GetRequiredService<IDbContextFactory<AuthorizationDbContext>>();
        await using var verify = await verifyContexts.CreateDbContextAsync();

        Assert.False(await verify.Applications.AnyAsync(a => a.ClientId == clientId));

        Assert.False(await verify.Tokens.AnyAsync(t => t.Application == canonical || t.Parent == canonical));
        Assert.False(await verify.Securities.AnyAsync(s => s.Parent == canonical));
    }

    [Fact]
    public async Task Delete_Then_Read_And_Replace_With_The_Same_Rat_Are_Unauthorized() {
        using var host   = factory.WithEnvironment("Testing");
        using var client = host.CreateClient();

        var (clientId, rat, _) = await RegisterAsync(client, new() {
            ["redirect_uris"] = new[] { "https://rp.example/cb" },
        });

        var (deleted, _) = await DeleteAsync(client, clientId, rat);
        Assert.Equal(HttpStatusCode.NoContent, deleted);

        var (readStatus, _, _) = await ReadAsync(client, clientId, rat);
        Assert.Equal(HttpStatusCode.Unauthorized, readStatus);

        var (replaceStatus, _, _) = await ReplaceAsync(client, clientId, new() {
            ["client_id"]     = clientId,
            ["redirect_uris"] = new[] { "https://rp.example/cb" },
        }, rat);
        Assert.Equal(HttpStatusCode.Unauthorized, replaceStatus);

        // A second delete with the retired token is equally unauthorized; nothing is resurrected.
        var (again, _) = await DeleteAsync(client, clientId, rat);
        Assert.Equal(HttpStatusCode.Unauthorized, again);
    }

    [Fact]
    public async Task Replace_Twice_With_The_Same_Rat_Leaves_The_Second_Replacement_Readable() {
        using var host   = factory.WithEnvironment("Testing");
        using var client = host.CreateClient();
        var (clientId, rat, _) = await RegisterAsync(client, new() {
            ["redirect_uris"] = new[] { "https://rp.example/initial" },
        });

        var (firstStatus, _, _) = await ReplaceAsync(client, clientId, new() {
            ["client_id"]     = clientId,
            ["redirect_uris"] = new[] { "https://rp.example/first" },
        }, rat);
        Assert.Equal(HttpStatusCode.OK, firstStatus);

        var (secondStatus, secondBody, _) = await ReplaceAsync(client, clientId, new() {
            ["client_id"]     = clientId,
            ["redirect_uris"] = new[] { "https://rp.example/second" },
        }, rat);
        Assert.Equal(HttpStatusCode.OK, secondStatus);

        var replacement = JsonDocument.Parse(secondBody).RootElement;
        Assert.Equal("https://rp.example/second", replacement.GetProperty("redirect_uris")[0].GetString());
        var (readStatus, readBody, _) = await ReadAsync(client, clientId, rat);
        Assert.Equal(HttpStatusCode.OK, readStatus);
        Assert.Equal(
            "https://rp.example/second",
            JsonDocument.Parse(readBody).RootElement.GetProperty("redirect_uris")[0].GetString());
    }

    [Fact]
    public async Task Expired_Registration_Token_Is_Rejected() {
        using var host = factory.WithEnvironment("Testing")
                                .WithServices(services => services.PostConfigure<SchemataAuthorizationOptions>(
                                                 options => options.RegistrationTokenLifetime = TimeSpan.Zero));
        using var client = host.CreateClient();

        var (clientId, rat, _) = await RegisterAsync(client, new() {
            ["redirect_uris"] = new[] { "https://rp.example/cb" },
        });

        var (status, _, challenge) = await ReadAsync(client, clientId, rat);
        Assert.Equal(HttpStatusCode.Unauthorized, status);
        Assert.Equal("Bearer error=\"invalid_token\"", challenge);
    }

    [Fact]
    public async Task Finite_Lifetime_Applies_To_Every_Successor() {
        using var host = factory.WithEnvironment("Testing")
                                .WithServices(services => services.PostConfigure<SchemataAuthorizationOptions>(
                                                 options => {
                                                     options.RegistrationTokenLifetime = TimeSpan.FromHours(2);
                                                     options.RotateRegistrationTokens  = true;
                                                 }));
        using var client = host.CreateClient();

        var (clientId, rat1, _) = await RegisterAsync(client, new() {
            ["redirect_uris"] = new[] { "https://rp.example/cb" },
        });

        var before = DateTime.UtcNow;
        var (status, body, _) = await ReadAsync(client, clientId, rat1);
        Assert.Equal(HttpStatusCode.OK, status);
        var rat2 = JsonDocument.Parse(body).RootElement.GetProperty("registration_access_token").GetString();

        using var scope = host.Services.CreateScope();
        var contexts    = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AuthorizationDbContext>>();
        await using var context = await contexts.CreateDbContextAsync();
        var successor = await context.Tokens.SingleAsync(t => t.ReferenceId == rat2);

        Assert.NotNull(successor.ExpireTime);
        Assert.True(successor.ExpireTime >= before.AddHours(2));
        Assert.True(successor.ExpireTime <= DateTime.UtcNow.AddHours(2));
    }

    private static async Task<(string ClientId, string Rat, JsonElement Created)> RegisterAsync(
        HttpClient                  client,
        Dictionary<string, object?> body
    ) {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/connect/register") {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new("Bearer", "initial-token");
        using var response = await client.SendAsync(request);
        var payload = await response.Content.ReadAsStringAsync();
        Assert.True(HttpStatusCode.Created == response.StatusCode, $"{(int)response.StatusCode}: {payload}");
        var root = JsonDocument.Parse(payload).RootElement.Clone();
        return (root.GetProperty("client_id").GetString()!,
            root.GetProperty("registration_access_token").GetString()!, root);
    }

    private static async Task<(HttpStatusCode Status, string Body, string? WwwAuthenticate)> ReadAsync(
        HttpClient client,
        string     clientId,
        string?    rat
    ) {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/connect/register/{clientId}");
        if (rat is not null) {
            request.Headers.Authorization = new("Bearer", rat);
        }

        using var response = await client.SendAsync(request);
        return (response.StatusCode, await response.Content.ReadAsStringAsync(),
            response.Headers.WwwAuthenticate.ToString() is { Length: > 0 } challenge ? challenge : null);
    }

    private static async Task<(HttpStatusCode Status, string Body, string? WwwAuthenticate)> ReplaceAsync(
        HttpClient                  client,
        string                      clientId,
        Dictionary<string, object?> body,
        string?                     rat
    ) {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/connect/register/{clientId}") {
            Content = JsonContent.Create(body),
        };
        if (rat is not null) {
            request.Headers.Authorization = new("Bearer", rat);
        }

        using var response = await client.SendAsync(request);
        return (response.StatusCode, await response.Content.ReadAsStringAsync(),
            response.Headers.WwwAuthenticate.ToString() is { Length: > 0 } challenge ? challenge : null);
    }

    private static async Task<(HttpStatusCode Status, string? WwwAuthenticate)> DeleteAsync(
        HttpClient client,
        string     clientId,
        string?    rat
    ) {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/connect/register/{clientId}");
        if (rat is not null) {
            request.Headers.Authorization = new("Bearer", rat);
        }

        using var response = await client.SendAsync(request);
        return (response.StatusCode,
            response.Headers.WwwAuthenticate.ToString() is { Length: > 0 } challenge ? challenge : null);
    }
}
