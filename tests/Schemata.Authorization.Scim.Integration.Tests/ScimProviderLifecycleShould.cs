using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Authorization.Skeleton.Managers;
using Schemata.Entity.Repository;
using Schemata.Scim;
using Schemata.Scim.Entities;
using Schemata.Security.Skeleton.Entities;
using Xunit;
using static Schemata.Authorization.Skeleton.AuthorizationConstants;

namespace Schemata.Authorization.Scim.Integration.Tests;

[Trait("Layer", "Integration")]
public sealed class ScimProviderLifecycleShould
{
    private const string Applications = "/scim/v2/Applications";
    private const string ApplicationSchema = "https://github.com/Cyprincess/Schemata/scim/schemas/2.0/Application";
    private const string AuthorizationSchema = "https://github.com/Cyprincess/Schemata/scim/schemas/2.0/ApplicationAuthorization";

    [Theory]
    [InlineData("EfCore")]
    [InlineData("LinqToDb")]
    public async Task Derived_Applications_Share_Manager_Metadata_And_Permissions(string provider) {
        using var host = new ProviderHost($"Provider{provider}");
        using var client = Client(host);
        using var other = Client(host, "domain-b");

        using (var schema = await client.GetAsync("/scim/v2/Schemas")) {
            var resources = (await JsonAsync(schema)).GetProperty("Resources").EnumerateArray();
            var application = resources.Single(resource => resource.GetProperty("id").GetString() == ApplicationSchema);
            var attributes = application.GetProperty("attributes").EnumerateArray()
                .ToDictionary(attribute => attribute.GetProperty("name").GetString()!);
            Assert.Equal("string", attributes["clientName"].GetProperty("type").GetString());
            Assert.Equal("complex", attributes["jwks"].GetProperty("type").GetString());
            Assert.True(attributes["jwks"].GetProperty("multiValued").GetBoolean());
            Assert.False(attributes.ContainsKey("clientSecret"));
            Assert.False(attributes.ContainsKey("registrationAccessToken"));
        }

        var body = ValidBody("Provider Application", "shared-application");
        body[AuthorizationSchema] = new { permissions = new[] { "e:/Connect/Token" } };
        var created = await SendAsync(client, HttpMethod.Post, Applications, body, privileged: true);
        Assert.Equal(HttpStatusCode.Created, created.Status);
        var root = created.Json();
        var id = root.GetProperty("id").GetString()!;
        var clientId = root.GetProperty("clientId").GetString()!;
        Assert.NotEqual(id, clientId);
        Assert.Equal("shared-application", root.GetProperty("externalId").GetString());
        AssertRedacted(root);

        var foreignBody = ValidBody("Foreign Application", "shared-application");
        var foreign = await SendAsync(other, HttpMethod.Post, Applications, foreignBody);
        Assert.Equal(HttpStatusCode.Created, foreign.Status);
        Assert.NotEqual(id, foreign.Json().GetProperty("id").GetString());
        var second = await SendAsync(client, HttpMethod.Post, Applications, ValidBody("Second Application", "second-application"));
        Assert.Equal(HttpStatusCode.Created, second.Status);

        using (var scope = host.Services.CreateScope()) {
            var manager = scope.ServiceProvider.GetRequiredService<IApplicationManager<ProviderApplication>>();
            var stored = await manager.FindByClientIdAsync(clientId);
            Assert.NotNull(stored);
            Assert.IsType<ProviderApplication>(stored);
            Assert.False(string.IsNullOrWhiteSpace(stored.Name));
            Assert.True(await manager.HasPermissionAsync(stored, "e:/Connect/Token"));
            await manager.SetClientNameAsync(stored, "Native manager");
            await manager.UpdateAsync(stored);
        }
        var nativeRead = await SendAsync(client, HttpMethod.Get, $"{Applications}/{id}");
        Assert.Equal("Native manager", nativeRead.Json().GetProperty("clientName").GetString());

        var page = await SendAsync(client, HttpMethod.Get, $"{Applications}?startIndex=2&count=1");
        Assert.Equal(3, page.Json().GetProperty("totalResults").GetInt32());
        Assert.Equal(1, page.Json().GetProperty("itemsPerPage").GetInt32());
        Assert.Single(page.Json().GetProperty("Resources").EnumerateArray());
        var filter = Uri.EscapeDataString("CLIENTNAME eq \"Native manager\"");
        var filtered = await SendAsync(client, HttpMethod.Get, $"{Applications}?filter={filter}&attributes=clientName");
        var projected = Assert.Single(filtered.Json().GetProperty("Resources").EnumerateArray());
        Assert.Equal("Native manager", projected.GetProperty("clientName").GetString());
        Assert.False(projected.TryGetProperty("grantTypes", out _));

        var deniedBody = ValidBody("Denied policy", "shared-application");
        deniedBody[AuthorizationSchema] = new { permissions = new[] { "e:/Connect/Introspect" } };
        var denied = await SendAsync(client, HttpMethod.Put, $"{Applications}/{id}", deniedBody);
        Assert.Equal(HttpStatusCode.Forbidden, denied.Status);
        Assert.Equal("Native manager", (await SendAsync(client, HttpMethod.Get, $"{Applications}/{id}")).Json()
            .GetProperty("clientName").GetString());

        var patched = await SendAsync(client, HttpMethod.Patch, $"{Applications}/{id}", new {
            schemas = new[] { ScimConstants.PatchOpSchema },
            Operations = new object[] {
                new { op = "replace", path = "clientName", value = "SCIM patched" },
                new { op = "replace", path = "externalId", value = "after-application" },
            },
        });
        Assert.Equal(HttpStatusCode.OK, patched.Status);
        Assert.Equal(id, patched.Json().GetProperty("id").GetString());
        Assert.Equal(clientId, patched.Json().GetProperty("clientId").GetString());
        AssertRedacted(patched.Json());
        using (var scope = host.Services.CreateScope()) {
            var manager = scope.ServiceProvider.GetRequiredService<IApplicationManager<ProviderApplication>>();
            var stored = await manager.FindByClientIdAsync(clientId);
            Assert.NotNull(stored);
            Assert.Equal("SCIM patched", stored.ClientName);
            Assert.True(await manager.HasPermissionAsync(stored, "e:/Connect/Token"));
        }

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(client, HttpMethod.Delete, $"{Applications}/{id}")).Status);
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(client, HttpMethod.Delete,
            $"{Applications}/{second.Json().GetProperty("id").GetString()}")).Status);
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(other, HttpMethod.Delete,
            $"{Applications}/{foreign.Json().GetProperty("id").GetString()}")).Status);
        using var verification = host.Services.CreateScope();
        Assert.Null(await verification.ServiceProvider.GetRequiredService<IApplicationManager<ProviderApplication>>()
            .FindByClientIdAsync(clientId));
        Assert.Equal(0, await verification.ServiceProvider.GetRequiredService<IRepository<ScimProvisioningBinding>>().CountAsync(query => query));
        Assert.Equal(0, await verification.ServiceProvider.GetRequiredService<IRepository<SchemataToken>>().CountAsync(query => query));
    }

    [Theory]
    [InlineData("EfCore")]
    [InlineData("LinqToDb")]
    public async Task Dcr_Manager_And_Scim_Share_One_OAuth_Client(string provider) {
        using var host = new ProviderHost($"Provider{provider}");
        using var client = Client(host);
        var registration = await RegisterAsync(client, new() {
            ["token_endpoint_auth_method"] = ClientAuthMethods.ClientSecretPost,
            ["grant_types"] = new[] { GrantTypes.ClientCredentials, GrantTypes.AuthorizationCode },
            ["response_types"] = new[] { ResponseTypes.Code },
            ["redirect_uris"] = new[] { "https://localhost/callback" },
            ["client_name"] = "DCR Application",
            ["scope"] = "openid api",
        });
        Assert.Equal(HttpStatusCode.Created, registration.Status);
        var registered = registration.Json();
        var clientId = registered.GetProperty("client_id").GetString()!;
        var clientSecret = registered.GetProperty("client_secret").GetString()!;
        var registrationToken = registered.GetProperty("registration_access_token").GetString()!;

        Guid uid;
        using (var scope = host.Services.CreateScope()) {
            var manager = scope.ServiceProvider.GetRequiredService<IApplicationManager<ProviderApplication>>();
            var stored = await manager.FindByClientIdAsync(clientId);
            Assert.NotNull(stored);
            Assert.IsType<ProviderApplication>(stored);
            uid = stored.Uid;
            await manager.SetClientNameAsync(stored, "Manager renamed");
            await manager.UpdateAsync(stored);
        }
        var managerDcr = await ReadRegistrationAsync(client, clientId, registrationToken);
        Assert.Equal("Manager renamed", managerDcr.Json().GetProperty("client_name").GetString());
        var managerScim = await SendAsync(client, HttpMethod.Get, $"{Applications}/{uid}");
        Assert.Equal("Manager renamed", managerScim.Json().GetProperty("clientName").GetString());
        Assert.False(managerScim.Json().TryGetProperty("registrationAccessToken", out _));

        var replacement = new Dictionary<string, object?> {
            ["schemas"] = new[] { ApplicationSchema },
            ["tokenEndpointAuthMethod"] = ClientAuthMethods.ClientSecretPost,
            ["redirectUris"] = new[] { "https://localhost/callback" },
            ["grantTypes"] = new[] { GrantTypes.ClientCredentials, GrantTypes.AuthorizationCode },
            ["responseTypes"] = new[] { ResponseTypes.Code },
            ["clientName"] = "SCIM renamed",
            ["externalId"] = "dcr-external",
            ["scope"] = "openid api",
            [AuthorizationSchema] = new { permissions = new[] { "e:/Connect/Token" } },
        };
        var replaced = await SendAsync(client, HttpMethod.Put, $"{Applications}/{uid}", replacement, privileged: true);
        Assert.Equal(HttpStatusCode.OK, replaced.Status);
        var scimDcr = await ReadRegistrationAsync(client, clientId, registrationToken);
        Assert.Equal("SCIM renamed", scimDcr.Json().GetProperty("client_name").GetString());
        using (var scope = host.Services.CreateScope()) {
            var manager = scope.ServiceProvider.GetRequiredService<IApplicationManager<ProviderApplication>>();
            var stored = await manager.FindByClientIdAsync(clientId);
            Assert.NotNull(stored);
            Assert.True(await manager.HasPermissionAsync(stored, "e:/Connect/Token"));
        }

        using var token = await client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string> {
            ["grant_type"] = GrantTypes.ClientCredentials,
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
        }));
        Assert.Equal(HttpStatusCode.OK, token.StatusCode);
        using var tokenJson = JsonDocument.Parse(await token.Content.ReadAsStringAsync());
        Assert.False(string.IsNullOrWhiteSpace(tokenJson.RootElement.GetProperty("access_token").GetString()));

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(client, HttpMethod.Delete, $"{Applications}/{uid}")).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await ReadRegistrationAsync(client, clientId, registrationToken)).Status);
        using var verification = host.Services.CreateScope();
        Assert.Null(await verification.ServiceProvider.GetRequiredService<IApplicationManager<ProviderApplication>>()
            .FindByClientIdAsync(clientId));
        Assert.Equal(0, await verification.ServiceProvider.GetRequiredService<IRepository<ScimProvisioningBinding>>().CountAsync(query => query));
        Assert.Equal(0, await verification.ServiceProvider.GetRequiredService<IRepository<SchemataToken>>().CountAsync(query => query));
    }

    [Theory]
    [InlineData("EfCore")]
    [InlineData("LinqToDb")]
    public async Task Failed_Application_Write_Preserves_Manager_And_Scim_Views(string provider) {
        using var host = new ProviderHost($"Provider{provider}");
        using var client = Client(host);
        var created = await SendAsync(client, HttpMethod.Post, Applications, ValidBody("Before failure", "before-external"));
        Assert.Equal(HttpStatusCode.Created, created.Status);
        var id = created.Json().GetProperty("id").GetString()!;
        var clientId = created.Json().GetProperty("clientId").GetString()!;

        var failed = await SendAsync(client, HttpMethod.Patch, $"{Applications}/{id}", new {
            schemas = new[] { ScimConstants.PatchOpSchema },
            Operations = new object[] {
                new { op = "replace", path = "clientName", value = "After failure" },
                new { op = "replace", path = "externalId", value = "fail-binding" },
            },
        });
        Assert.True(failed.Status == HttpStatusCode.InternalServerError, failed.Body);

        var scim = await SendAsync(client, HttpMethod.Get, $"{Applications}/{id}");
        Assert.Equal("Before failure", scim.Json().GetProperty("clientName").GetString());
        Assert.Equal("before-external", scim.Json().GetProperty("externalId").GetString());
        using var verification = host.Services.CreateScope();
        var stored = await verification.ServiceProvider.GetRequiredService<IApplicationManager<ProviderApplication>>()
            .FindByClientIdAsync(clientId);
        Assert.NotNull(stored);
        Assert.Equal("Before failure", stored.ClientName);
        var binding = await verification.ServiceProvider.GetRequiredService<IRepository<ScimProvisioningBinding>>()
            .SingleOrDefaultAsync(query => query.Where(row => row.EntityUid == stored.Uid));
        Assert.NotNull(binding);
        Assert.Equal("before-external", binding.ExternalId);
    }

    private static Dictionary<string, object?> ValidBody(string name, string externalId) {
        return new() {
            ["schemas"] = new[] { ApplicationSchema },
            ["tokenEndpointAuthMethod"] = ClientAuthMethods.None,
            ["redirectUris"] = new[] { "https://localhost/callback" },
            ["grantTypes"] = new[] { GrantTypes.AuthorizationCode, GrantTypes.RefreshToken },
            ["responseTypes"] = new[] { ResponseTypes.Code },
            ["clientName"] = name,
            ["externalId"] = externalId,
            ["scope"] = "openid offline_access",
        };
    }

    private static HttpClient Client(ProviderHost host, string domain = "domain-a") {
        var client = host.CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Test-Scim", "1");
        client.DefaultRequestHeaders.Add("X-Test-Domain", domain);
        return client;
    }

    private static async Task<Response> SendAsync(
        HttpClient client,
        HttpMethod method,
        string uri,
        object? body = null,
        bool privileged = false
    ) {
        using var request = new HttpRequestMessage(method, uri);
        if (body is not null) request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, ScimConstants.MediaType);
        if (privileged) request.Headers.Add("X-Test-Permissions", "allow");
        using var response = await client.SendAsync(request);
        return new(response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private static async Task<Response> RegisterAsync(HttpClient client, Dictionary<string, object?> body) {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/connect/register") {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new("Bearer", "initial-token");
        using var response = await client.SendAsync(request);
        return new(response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private static async Task<Response> ReadRegistrationAsync(HttpClient client, string clientId, string token) {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/connect/register/{clientId}");
        request.Headers.Authorization = new("Bearer", token);
        using var response = await client.SendAsync(request);
        return new(response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static void AssertRedacted(JsonElement root) {
        foreach (var field in new[] { "clientSecret", "clientSecretExpiresAt", "registrationAccessToken", "registrationClientUri", "accessToken", "refreshToken", "password" }) {
            Assert.False(root.TryGetProperty(field, out _));
        }
    }

    private sealed record Response(HttpStatusCode Status, string Body)
    {
        public JsonElement Json() {
            using var document = JsonDocument.Parse(Body);
            return document.RootElement.Clone();
        }
    }

    private sealed class ProviderHost(string environment) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) {
            builder.UseEnvironment(environment);
            builder.UseContentRoot(Path.GetDirectoryName(typeof(ProviderHost).Assembly.Location)!);
        }
    }
}
