using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Schemata.Authorization.Foundation.Features;
using Schemata.Authorization.Foundation.Services;
using Schemata.Authorization.Integration.Tests.Fixtures;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Authorization.Skeleton.Services;
using Schemata.Core;
using Schemata.Security.Skeleton;
using Schemata.Security.Skeleton.Entities;
using Schemata.Security.Skeleton.Services;
using Xunit;
using static Schemata.Abstractions.SchemataConstants;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Integration.Tests;

[Trait("Category", "Integration")]
[Trait("Layer", "Integration")]
public class ResourceOwnerPasswordFlowShould
{
    private const string ClientId = "password-client";
    private const string ClientSecret = "password-client-secret";
    private const string UserPassword = "user-password-DO-NOT-LEAK";

    [Fact]
    public async Task Unregistered_Grant_Is_Unsupported_And_Not_Advertised() {
        using var host = new WebAppFactory();
        await SeedClient(host);
        using var client = host.CreateClient();

        using var response = await client.PostAsync(Endpoints.Token, TokenRequest());
        using var discoveryResponse = await client.GetAsync("/.well-known/openid-configuration");

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(OAuthErrors.UnsupportedGrantType, await Error(response));
        using var discovery = await JsonDocument.ParseAsync(await discoveryResponse.Content.ReadAsStreamAsync());
        Assert.DoesNotContain(GrantTypes.Password, Grants(discovery.RootElement));
    }

    [Fact]
    public async Task Valid_Client_And_User_Receive_Token_Without_Cookie_Or_Credential_Disclosure() {
        var validator = new TestValidator(Principal());
        var logs = new CaptureLoggerProvider();
        using var host = Host(validator, logs);
        await SeedClient(host);
        using var client = host.CreateClient();

        using var response = await client.PostAsync(Endpoints.Token, TokenRequest());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var payload = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.False(string.IsNullOrWhiteSpace(payload.RootElement.GetProperty("access_token").GetString()));
        Assert.Equal("api", payload.RootElement.GetProperty("scope").GetString());
        Assert.False(payload.RootElement.TryGetProperty("id_token", out _));
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out _));
        Assert.Equal(1, validator.Calls);

        using var discoveryResponse = await client.GetAsync("/.well-known/openid-configuration");
        using var discovery = await JsonDocument.ParseAsync(await discoveryResponse.Content.ReadAsStreamAsync());
        Assert.Contains(GrantTypes.Password, Grants(discovery.RootElement));

        using var scope = host.Services.CreateScope();
        var contexts = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AuthorizationDbContext>>();
        await using var context = await contexts.CreateDbContextAsync();
        var token = await context.Tokens.SingleAsync();
        Assert.Equal("users/alice", token.Parent);
        Assert.Equal("applications/password-client", token.Application);
        var names = await context.Applications.Select(value => value.Name).ToListAsync();
        names.AddRange(await context.Securities.Select(value => value.Name).ToListAsync());
        names.AddRange(await context.Tokens.Select(value => value.Name).ToListAsync());
        Assert.All(names, value => Assert.DoesNotContain(UserPassword, value, StringComparison.Ordinal));
        Assert.NotEmpty(logs.Messages);
        Assert.All(logs.Messages, value => Assert.DoesNotContain(UserPassword, value, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Invalid_Client_Credentials_Do_Not_Invoke_User_Validation() {
        var validator = new TestValidator(Principal());
        using var host = Host(validator);
        await SeedClient(host);
        using var client = host.CreateClient();

        using var response = await client.PostAsync(Endpoints.Token, TokenRequest(clientSecret: "wrong-secret"));

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(OAuthErrors.InvalidClient, await Error(response));
        Assert.Equal(0, validator.Calls);
        Assert.Equal(0, await TokenCount(host));
    }

    [Fact]
    public async Task Invalid_User_Credentials_Return_Invalid_Grant() {
        var validator = new TestValidator(Principal());
        using var host = Host(validator);
        await SeedClient(host);
        using var client = host.CreateClient();

        using var response = await client.PostAsync(Endpoints.Token, TokenRequest(password: "wrong-user-password"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(OAuthErrors.InvalidGrant, await Error(response));
        Assert.Equal(1, validator.Calls);
        Assert.Equal(0, await TokenCount(host));
    }

    [Fact]
    public async Task Missing_Validator_Returns_Invalid_Grant() {
        using var host = Host(null);
        await SeedClient(host);
        using var client = host.CreateClient();

        using var response = await client.PostAsync(Endpoints.Token, TokenRequest());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(OAuthErrors.InvalidGrant, await Error(response));
        Assert.Equal(0, await TokenCount(host));
    }

    [Fact]
    public async Task Client_Without_Password_Grant_Metadata_Is_Rejected() {
        var validator = new TestValidator(Principal());
        using var host = Host(validator);
        await SeedClient(host, grantTypes: [GrantTypes.ClientCredentials]);
        using var client = host.CreateClient();

        using var response = await client.PostAsync(Endpoints.Token, TokenRequest());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(OAuthErrors.UnauthorizedClient, await Error(response));
        Assert.Equal(0, validator.Calls);
        Assert.Equal(0, await TokenCount(host));
    }

    private static WebAppFactory Host(TestValidator? validator, CaptureLoggerProvider? logs = null) {
        return new WebAppFactory().WithServices(services => {
            var schemata = new SchemataOptions();
            var configurators = new Configurators();
            new ResourceOwnerPasswordFlowFeature<SchemataApplication>().ConfigureServices(services, schemata, configurators);
            if (validator is not null) {
                services.AddSingleton<IResourceOwnerPasswordValidator>(validator);
            }
            if (logs is not null) {
                services.AddSingleton<ILoggerProvider>(logs);
            }
        });
    }

    private static async Task SeedClient(WebAppFactory host, string[]? grantTypes = null) {
        using var scope = host.Services.CreateScope();
        var applications = scope.ServiceProvider.GetRequiredService<IApplicationManager<SchemataApplication>>();
        var securities = scope.ServiceProvider.GetRequiredService<ISecurityStore<SchemataSecurity>>();
        var verifier = scope.ServiceProvider.GetRequiredService<ISecretVerifier>();
        var application = new SchemataApplication {
            Name = ClientId,
            ClientId = ClientId,
            TokenEndpointAuthMethod = ClientAuthMethods.ClientSecretPost,
            Permissions = ["e:/Connect/Token"],
            GrantTypes = grantTypes ?? [GrantTypes.Password],
            Scope = "api",
        };
        await applications.CreateAsync(application);
        await securities.CreateAsync(new() {
            Parent = SecurityParents.Application(application),
            Key = ClientId,
            Kind = SecurityConstants.Kinds.Password,
            Usage = SecurityConstants.Usages.Authentication,
            Algorithm = SecurityConstants.Algorithms.Pbkdf2,
            Value = await verifier.HashAsync(ClientSecret),
            Status = SecurityConstants.Statuses.Valid,
        });
    }

    private static FormUrlEncodedContent TokenRequest(
        string clientId = ClientId,
        string clientSecret = ClientSecret,
        string username = "alice",
        string password = UserPassword
    ) {
        return new(new Dictionary<string, string> {
            ["grant_type"] = GrantTypes.Password,
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["username"] = username,
            ["password"] = password,
            ["scope"] = "api",
        });
    }

    private static ClaimsPrincipal Principal() {
        return new(new ClaimsIdentity([
            new Claim(IdentityClaims.Subject, "users/alice"),
            new Claim(IdentityClaims.PreferredUsername, "alice"),
        ], "Identity.Application"));
    }

    private static async Task<string?> Error(HttpResponseMessage response) {
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return document.RootElement.GetProperty("error").GetString();
    }

    private static string[] Grants(JsonElement discovery) {
        return discovery.GetProperty("grant_types_supported").EnumerateArray().Select(value => value.GetString()!).ToArray();
    }

    private static async Task<int> TokenCount(WebAppFactory host) {
        using var scope = host.Services.CreateScope();
        var contexts = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AuthorizationDbContext>>();
        await using var context = await contexts.CreateDbContextAsync();
        return await context.Tokens.CountAsync();
    }

    private sealed class TestValidator(ClaimsPrincipal principal) : IResourceOwnerPasswordValidator
    {
        public int Calls { get; private set; }

        public Task<ClaimsPrincipal?> ValidateAsync(
            SchemataApplication application,
            string username,
            string password,
            CancellationToken ct
        ) {
            Calls++;
            return Task.FromResult(username == "alice" && password == UserPassword ? principal : null);
        }
    }

    private sealed class CaptureLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();

        public ILogger CreateLogger(string categoryName) {
            return new CaptureLogger(Messages);
        }

        public void Dispose() { }
    }

    private sealed class CaptureLogger(ConcurrentQueue<string> messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel) {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        ) {
            messages.Enqueue(formatter(state, exception));
            if (exception is not null) {
                messages.Enqueue(exception.ToString());
            }
        }
    }
}
