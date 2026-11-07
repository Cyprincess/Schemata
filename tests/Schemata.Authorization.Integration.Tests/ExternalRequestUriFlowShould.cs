using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
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
public sealed class ExternalRequestUriFlowShould
{
    [Fact]
    public async Task Retrieve_A_Registered_Https_Object_And_Require_Registration_In_Discovery() {
        using var clientKey = RSA.Create(2048);
        var assertion = Mint(clientKey);
        var uri = Commit("https://rp.example/request", assertion);
        var fetches = 0;
        await using var server = await Endpoint(_ => {
            fetches++;
            return assertion;
        });
        await using var factory = Factory(server);
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        await SeedRegistration(factory, clientKey, [uri]);

        using var response = await client.GetAsync(Endpoints.Authorize
            + "?client_id=code-client&response_type=code&scope=openid&request_uri=" + Uri.EscapeDataString(uri));
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(1, fetches);

        using var discovery = await JsonDocument.ParseAsync(
            await client.GetStreamAsync("/.well-known/openid-configuration"));
        Assert.True(discovery.RootElement.GetProperty("request_uri_parameter_supported").GetBoolean());
        Assert.True(discovery.RootElement.GetProperty("require_request_uri_registration").GetBoolean());
    }

    [Fact]
    public async Task Reject_Malformed_Retrieved_Content_As_A_Request_Object() {
        using var clientKey = RSA.Create(2048);
        const string uri = "https://rp.example/malformed";
        await using var server = await Endpoint(_ => "not-a-jwt");
        await using var factory = Factory(server);
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        await SeedRegistration(factory, clientKey, [uri]);

        using var response = await client.GetAsync(Endpoints.Authorize
            + "?client_id=code-client&request_uri=" + Uri.EscapeDataString(uri));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var error = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal(OAuthErrors.InvalidRequestObject, error.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Keep_Local_Par_Redemption_When_External_Resolution_Is_Enabled() {
        using var clientKey = RSA.Create(2048);
        await using var server = await Endpoint(_ => "unused");
        await using var factory = Factory(server);
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        await SeedRegistration(factory, clientKey, []);

        using var form = new FormUrlEncodedContent(new Dictionary<string, string> {
            [Parameters.ClientId] = "code-client",
            [Parameters.ClientSecret] = "code-secret",
            [Parameters.Request] = Mint(clientKey),
        });
        using var pushed = await client.PostAsync(Endpoints.Par, form);
        Assert.Equal(HttpStatusCode.Created, pushed.StatusCode);
        using var par = await JsonDocument.ParseAsync(await pushed.Content.ReadAsStreamAsync());
        var requestUri = par.RootElement.GetProperty("request_uri").GetString();
        Assert.NotNull(requestUri);
        Assert.StartsWith(RequestUriPrefixes.Par, requestUri);

        using var response = await client.GetAsync(Endpoints.Authorize
            + "?client_id=code-client&request_uri=" + Uri.EscapeDataString(requestUri));
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
    }

    private static WebAppFactory Factory(WebApplication server) {
        return new WebAppFactory().WithEnvironment("ExternalJar").WithServices(services => {
            services.Configure<JwtSecuredAuthorizationRequestsOptions>(options =>
                options.SigningAlgorithms.Add(SigningAlgorithms.RsaSha256));
            services.AddHttpClient(ExternalRequestObjectResolver.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => server.GetTestServer().CreateHandler());
        });
    }

    private static async Task<WebApplication> Endpoint(Func<HttpContext, string> content) {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        var app = builder.Build();
        app.Run(async context => {
            context.Response.ContentType = "application/oauth-authz-req+jwt";
            await context.Response.WriteAsync(content(context), Encoding.UTF8);
        });
        await app.StartAsync();
        return app;
    }

    private static async Task SeedRegistration(
        WebAppFactory factory,
        RSA clientKey,
        ICollection<string> requestUris
    ) {
        await using var scope = factory.Services.CreateAsyncScope();
        var applications = scope.ServiceProvider.GetRequiredService<IApplicationManager<SchemataApplication>>();
        var application = await applications.FindByClientIdAsync("code-client");
        Assert.NotNull(application);
        application.RequestObjectSigningAlg = SigningAlgorithms.RsaSha256;
        application.RequestUris = requestUris;
        await applications.UpdateAsync(application);

        var parameters = clientKey.ExportParameters(false);
        var securities = scope.ServiceProvider.GetRequiredService<ISecurityStore<SchemataSecurity>>();
        await securities.CreateAsync(new() {
            Parent = SecurityParents.Application(application),
            Key = "external-jar-client",
            Kind = SecurityConstants.Kinds.Jwks,
            Usage = SecurityConstants.Usages.Authentication,
            Status = SecurityConstants.Statuses.Valid,
            Value = JsonSerializer.Serialize(new {
                keys = new[] {
                    new {
                        kty = "RSA",
                        kid = "external-jar-client",
                        use = "sig",
                        n = Base64UrlEncoder.Encode(parameters.Modulus!),
                        e = Base64UrlEncoder.Encode(parameters.Exponent!),
                    },
                },
            }),
        });
    }

    private static string Mint(RSA clientKey) {
        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(new SecurityTokenDescriptor {
            Issuer = "code-client",
            Audience = "https://localhost",
            Claims = new Dictionary<string, object> {
                [Parameters.ClientId] = "code-client",
                [Parameters.RedirectUri] = "https://localhost/callback",
                [Parameters.ResponseType] = ResponseTypes.Code,
                [Parameters.Scope] = Scopes.OpenId,
                [Parameters.State] = "external-jar",
                [Parameters.CodeChallenge] = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM",
                [Parameters.CodeChallengeMethod] = "S256",
            },
            SigningCredentials = new(
                new RsaSecurityKey(clientKey) { KeyId = "external-jar-client" },
                SigningAlgorithms.RsaSha256),
        });
    }

    private static string Commit(string uri, string body) {
        return uri + "#" + Base64UrlEncoder.Encode(SHA256.HashData(Encoding.UTF8.GetBytes(body)));
    }
}
