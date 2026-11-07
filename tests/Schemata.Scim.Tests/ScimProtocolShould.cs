using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Schemata.Entity.Repository;
using Schemata.Scim.Entities;
using Schemata.Scim.Models;
using Schemata.Scim.Services;
using Xunit;

namespace Schemata.Scim.Tests;

[Trait("Layer", "Integration")]
public sealed class ScimProtocolShould
{
    [Fact]
    public async Task Resource_Operations_Use_Scim_Json_And_Stable_Server_Ids() {
        using var host = await StartHostAsync();
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-Auth", "yes");

        using var created = await SendAsync(client, HttpMethod.Post, "/scim/v2/Users", new JsonObject {
            ["schemas"] = new JsonArray(ScimConstants.UserSchema),
            ["id"] = "client-id",
            ["userName"] = "alice",
            ["displayName"] = "Alice",
            ["unknownOptional"] = "ignored",
            ["groups"] = new JsonArray(new JsonObject { ["value"] = "client-group" }),
        });
        var createdBody = await ReadAsync(created);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(ScimConstants.MediaType, created.Content.Headers.ContentType?.MediaType);
        Assert.NotEqual("client-id", createdBody["id"]?.GetValue<string>());
        Assert.Equal("alice", createdBody["userName"]?.GetValue<string>());
        Assert.False(createdBody.ContainsKey("unknownOptional"));
        Assert.False(createdBody.ContainsKey("groups"));
        Assert.Equal(createdBody["meta"]?["location"]?.GetValue<string>(), created.Headers.Location?.ToString());
        Assert.False(createdBody["meta"]!.AsObject().ContainsKey("version"));

        var id = createdBody["id"]!.GetValue<string>();
        using var replaced = await SendAsync(client, HttpMethod.Put, $"/scim/v2/Users/{id}", new JsonObject {
            ["id"] = "replacement-id",
            ["userName"] = "alice-renamed",
            ["displayName"] = "Replacement",
            ["groups"] = new JsonArray(new JsonObject { ["value"] = "client-group" }),
        });
        var replacedBody = await ReadAsync(replaced);

        Assert.Equal(HttpStatusCode.OK, replaced.StatusCode);
        Assert.Equal(id, replacedBody["id"]?.GetValue<string>());
        Assert.Equal("alice-renamed", replacedBody["userName"]?.GetValue<string>());
        Assert.False(replacedBody.ContainsKey("groups"));

        using var patched = await SendAsync(client, HttpMethod.Patch, $"/scim/v2/Users/{id}", new JsonObject {
            ["schemas"] = new JsonArray(ScimConstants.PatchOpSchema),
            ["Operations"] = new JsonArray(new JsonObject { ["op"] = "replace", ["path"] = "displayName", ["value"] = "Patched" }),
        });
        Assert.Equal("Patched", (await ReadAsync(patched))["displayName"]?.GetValue<string>());

        using var deleted = await client.DeleteAsync($"/scim/v2/Users/{id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        using var missing = await client.GetAsync($"/scim/v2/Users/{id}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Empty_And_Paginated_Searches_Return_ListResponses() {
        using var host = await StartHostAsync();
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-Auth", "yes");

        using (var empty = await client.GetAsync("/scim/v2/Users")) {
            var body = await ReadAsync(empty);
            Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
            Assert.Equal(0, body["totalResults"]?.GetValue<int>());
            Assert.Equal(0, body["Resources"]?.AsArray().Count);
        }

        await CreateUserAsync(client, "alice");
        await CreateUserAsync(client, "bob");

        using var zero = await client.GetAsync("/scim/v2/Users?startIndex=0&count=0");
        var zeroBody = await ReadAsync(zero);
        Assert.Equal(1, zeroBody["startIndex"]?.GetValue<int>());
        Assert.Equal(2, zeroBody["totalResults"]?.GetValue<int>());
        Assert.Equal(0, zeroBody["itemsPerPage"]?.GetValue<int>());
        Assert.Empty(zeroBody["Resources"]!.AsArray());

        using var negative = await client.GetAsync("/scim/v2/Users?count=-4");
        Assert.Equal(0, (await ReadAsync(negative))["Resources"]?.AsArray().Count);

        using var search = await SendAsync(client, HttpMethod.Post, "/scim/v2/Users/.search", new JsonObject {
            ["schemas"] = new JsonArray(ScimConstants.SearchRequestSchema),
            ["filter"] = "userName eq \"ALICE\"",
            ["attributes"] = new JsonArray("userName"),
        });
        var searchBody = await ReadAsync(search);
        Assert.Equal(HttpStatusCode.OK, search.StatusCode);
        Assert.Equal(1, searchBody["totalResults"]?.GetValue<int>());
        Assert.Equal("alice", searchBody["Resources"]?[0]?["userName"]?.GetValue<string>());
        Assert.False(searchBody["Resources"]![0]!.AsObject().ContainsKey("displayName"));
    }

    [Fact]
    public async Task Put_Does_Not_Create_A_Missing_Resource() {
        using var host = await StartHostAsync();
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-Auth", "yes");

        using var response = await SendAsync(client, HttpMethod.Put, "/scim/v2/Users/missing", new JsonObject { ["userName"] = "new-user" });
        var body = await ReadAsync(response);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ScimConstants.ErrorSchema, body["schemas"]?[0]?.GetValue<string>());
        using var list = await client.GetAsync("/scim/v2/Users");
        Assert.Equal(0, (await ReadAsync(list))["totalResults"]?.GetValue<int>());
    }

    [Fact]
    public async Task Authentication_Uniqueness_And_Malformed_Filter_Errors_Have_Exact_Shapes() {
        using var host = await StartHostAsync();
        using var client = host.GetTestClient();

        using (var anonymous = await client.GetAsync("/scim/v2/Users")) {
            await AssertErrorAsync(anonymous, HttpStatusCode.Unauthorized, null);
        }

        client.DefaultRequestHeaders.Add("X-Test-Auth", "yes");
        await CreateUserAsync(client, "alice");
        using (var duplicate = await SendAsync(client, HttpMethod.Post, "/scim/v2/Users", new JsonObject { ["userName"] = "ALICE" })) {
            await AssertErrorAsync(duplicate, HttpStatusCode.Conflict, ScimConstants.Uniqueness);
        }

        using (var malformed = await client.GetAsync("/scim/v2/Users?filter=userName%20regex%20%22alice%22")) {
            await AssertErrorAsync(malformed, HttpStatusCode.BadRequest, ScimConstants.InvalidFilter);
        }
    }

    [Fact]
    public async Task Password_Input_Returns_InvalidValue_Without_Echoing_The_Secret() {
        using var host = await StartHostAsync();
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-Auth", "yes");

        using var response = await SendAsync(client, HttpMethod.Post, "/scim/v2/Users", new JsonObject {
            ["userName"] = "alice",
            ["password"] = "a-secret-value",
        });
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("a-secret-value", content, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(content);
        Assert.Equal(ScimConstants.InvalidValue, document.RootElement.GetProperty("scimType").GetString());
    }

    [Fact]
    public async Task Unsupported_Capabilities_Return_Scim_Errors_Without_Advertising_Extensions() {
        using var host = await StartHostAsync();
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-Auth", "yes");

        foreach (var path in new[] { "/scim/v2/Bulk", "/scim/v2/.search", "/scim/v2/Me" }) {
            using var response = await SendAsync(client, HttpMethod.Post, path, new JsonObject());
            Assert.Equal(ScimConstants.MediaType, response.Content.Headers.ContentType?.MediaType);
            Assert.Equal(ScimConstants.ErrorSchema, (await ReadAsync(response))["schemas"]?[0]?.GetValue<string>());
        }

        using var discovery = await client.GetAsync("/scim/v2/ServiceProviderConfig");
        var config = await ReadAsync(discovery);
        Assert.False(config["bulk"]!["supported"]?.GetValue<bool>());
        Assert.False(config["sort"]!["supported"]?.GetValue<bool>());
        Assert.False(config["changePassword"]!["supported"]?.GetValue<bool>());
        Assert.False(config["etag"]!["supported"]?.GetValue<bool>());
        Assert.False(config["meta"]!.AsObject().ContainsKey("version"));

        using var schema = await client.GetAsync("/scim/v2/Schemas");
        Assert.DoesNotContain(ScimConstants.EnterpriseUserSchema, (await ReadAsync(schema))["Resources"]!.AsArray()
            .Select(resource => resource!["id"]?.GetValue<string>()));
    }

    [Fact]
    public async Task Request_Limits_Are_Enforced_Before_Handler_Execution() {
        using var host = await StartHostAsync(services => services.Configure<ScimServiceProviderOptions>(options => {
            options.MaxPatchOperations = 1;
            options.MaxFilterLength = 16;
        }));
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-Auth", "yes");
        await CreateUserAsync(client, "alice");

        using var filter = await client.GetAsync("/scim/v2/Users?filter=userName%20eq%20%22alice%22");
        await AssertErrorAsync(filter, HttpStatusCode.BadRequest, ScimConstants.InvalidFilter);

        using var patch = await SendAsync(client, HttpMethod.Patch, "/scim/v2/Users/alice", new JsonObject {
            ["schemas"] = new JsonArray(ScimConstants.PatchOpSchema),
            ["Operations"] = new JsonArray(
                new JsonObject { ["op"] = "replace", ["path"] = "displayName", ["value"] = "One" },
                new JsonObject { ["op"] = "replace", ["path"] = "displayName", ["value"] = "Two" }),
        });
        await AssertErrorAsync(patch, HttpStatusCode.BadRequest, ScimConstants.InvalidValue);
    }

    [Fact]
    public async Task Oversized_Bodies_Return_413_Before_Provisioning() {
        using var host = await StartHostAsync(services => services.Configure<ScimServiceProviderOptions>(options => options.MaxRequestBodySize = 64));
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-Auth", "yes");

        using var response = await SendAsync(client, HttpMethod.Post, "/scim/v2/Users", new JsonObject {
            ["userName"] = new string('a', 128),
        });

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal(ScimConstants.ErrorSchema, (await ReadAsync(response))["schemas"]?[0]?.GetValue<string>());
        using var list = await client.GetAsync("/scim/v2/Users");
        Assert.Equal(0, (await ReadAsync(list))["totalResults"]?.GetValue<int>());
    }

    [Fact]
    public async Task Custom_BasePath_Applies_To_Resource_And_Unsupported_Routes() {
        using var host = await StartHostAsync(services => services.Configure<ScimServiceProviderOptions>(options => options.BasePath = "/custom/scim"));
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-Auth", "yes");

        using (var search = await SendAsync(client, HttpMethod.Post, "/custom/scim/Users/.search", new JsonObject {
            ["schemas"] = new JsonArray(ScimConstants.SearchRequestSchema),
        })) {
            Assert.Equal(HttpStatusCode.OK, search.StatusCode);
            Assert.Equal(0, (await ReadAsync(search))["totalResults"]?.GetValue<int>());
        }

        foreach (var path in new[] { "/custom/scim/.search", "/custom/scim/Bulk", "/custom/scim/Me" }) {
            using var response = await SendAsync(client, HttpMethod.Post, path, new JsonObject());
            Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
            Assert.Equal(ScimConstants.ErrorSchema, (await ReadAsync(response))["schemas"]?[0]?.GetValue<string>());
        }

        foreach (var path in new[] { "/scim/v2/Users/.search", "/scim/v2/.search", "/scim/v2/Bulk", "/scim/v2/Me" }) {
            using var response = await SendAsync(client, HttpMethod.Post, path, new JsonObject());
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    [Fact]
    public async Task Scoped_Handlers_Are_Resolved_Per_Request_For_Route_Selection_And_Invocation() {
        using var host = await StartHostAsync(scopedHandler: true);
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-Auth", "yes");

        using (var created = await SendAsync(client, HttpMethod.Post, "/scim/v2/Users", new JsonObject { ["userName"] = "scoped-user" })) {
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            Assert.Equal("scoped-user", (await ReadAsync(created))["userName"]?.GetValue<string>());
        }

        using var list = await client.GetAsync("/scim/v2/Users");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(0, (await ReadAsync(list))["totalResults"]?.GetValue<int>());
    }

    private static async Task<IHost> StartHostAsync(Action<IServiceCollection>? configure = null, bool scopedHandler = false) {
        return await new HostBuilder()
                    .ConfigureWebHost(web => web.UseEnvironment(scopedHandler ? Environments.Development : Environments.Production)
                                               .UseTestServer()
                                               .ConfigureServices(services => {
                                                    services.AddRouting();
                                                    services.AddControllers();
                                                    services.AddSchemataScim();
                                                    if (scopedHandler) {
                                                        services.AddScoped<IScimResourceHandler, InMemoryUserHandler>();
                                                        services.AddSingleton(Mock.Of<IRepository<ScimProvisioningBinding>>());
                                                    } else {
                                                        services.AddSingleton<IScimResourceHandler, InMemoryUserHandler>();
                                                    }
                                                    configure?.Invoke(services);
                                                })
                                               .Configure(app => {
                                                    app.UseSchemataScimErrors();
                                                    app.UseRouting();
                                                    app.Use(async (context, next) => {
                                                        if (context.Request.Headers.ContainsKey("X-Test-Auth")) {
                                                            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "test")], "test"));
                                                        }
                                                        await next();
                                                    });
                                                    app.UseEndpoints(endpoints => endpoints.MapControllers());
                                                }))
                    .StartAsync();
    }

    private static async Task CreateUserAsync(HttpClient client, string userName) {
        using var response = await SendAsync(client, HttpMethod.Post, "/scim/v2/Users", new JsonObject { ["userName"] = userName });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, JsonObject body) {
        var request = new HttpRequestMessage(method, path) {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, ScimConstants.MediaType),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(ScimConstants.MediaType));
        return client.SendAsync(request);
    }

    private static async Task<JsonObject> ReadAsync(HttpResponseMessage response) {
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
    }

    private static async Task AssertErrorAsync(HttpResponseMessage response, HttpStatusCode status, string? scimType) {
        var body = await ReadAsync(response);
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(ScimConstants.MediaType, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(ScimConstants.ErrorSchema, body["schemas"]?[0]?.GetValue<string>());
        Assert.Equal(((int)status).ToString(), body["status"]?.GetValue<string>());
        Assert.Equal(scimType, body["scimType"]?.GetValue<string>());
    }

    private sealed record JsonScimResource() : ScimResource([ScimConstants.UserSchema])
    {
        [JsonExtensionData]
        public Dictionary<string, JsonElement> Attributes { get; init; } = new();
    }

    private sealed class InMemoryUserHandler : IScimResourceHandler
    {
        private readonly Dictionary<string, JsonObject> _resources = new(StringComparer.Ordinal);

        public ScimResourceType ResourceType { get; } = new() {
            Id = "User", Name = "User", Endpoint = "/Users", Schema = ScimConstants.UserSchema, Description = "User Account",
        };

        public IReadOnlyList<ScimSchema> Schemas { get; } = [ScimSchemaRegistry.CoreUser];

        public Task<ScimResource?> GetAsync(string id, ClaimsPrincipal principal, CancellationToken ct) {
            return Task.FromResult(_resources.TryGetValue(id, out var resource) ? ToResource(resource) : null);
        }

        public Task<ScimSearchResult> SearchAsync(ScimSearchRequest request, ClaimsPrincipal principal, CancellationToken ct) {
            var matches = _resources.Values.Where(resource => request.FilterExpression is null
                    || ScimFilterEvaluator.Matches(request.FilterExpression, resource))
                .OrderBy(resource => resource["id"]?.GetValue<string>(), StringComparer.Ordinal)
                .ToList();
            var page = matches.Skip(request.StartIndex - 1).Take(request.Count).Select(resource => ToResource(resource)!).ToList();
            return Task.FromResult(new ScimSearchResult {
                TotalResults = matches.Count,
                StartIndex = request.StartIndex,
                Resources = page,
            });
        }

        public Task<ScimResource> CreateAsync(JsonObject body, ClaimsPrincipal principal, CancellationToken ct) {
            var userName = body["userName"]?.GetValue<string>() ?? throw new ScimProtocolException(400, ScimConstants.InvalidValue, "userName is required.");
            if (_resources.Values.Any(resource => string.Equals(resource["userName"]?.GetValue<string>(), userName, StringComparison.OrdinalIgnoreCase))) {
                throw new ScimProtocolException(409, ScimConstants.Uniqueness, "userName must be unique.");
            }

            var resource = body.DeepClone().AsObject();
            resource["id"] = Guid.NewGuid().ToString("N");
            _resources.Add(resource["id"]!.GetValue<string>(), resource);
            return Task.FromResult(ToResource(resource)!);
        }

        public Task<ScimResource?> ReplaceAsync(string id, JsonObject body, ClaimsPrincipal principal, CancellationToken ct) {
            if (!_resources.TryGetValue(id, out var current)) {
                return Task.FromResult<ScimResource?>(null);
            }

            var resource = body.DeepClone().AsObject();
            resource["id"] = id;
            resource["schemas"] = current["schemas"]?.DeepClone();
            _resources[id] = resource;
            return Task.FromResult(ToResource(resource));
        }

        public Task<ScimResource?> PatchAsync(string id, ScimPatchDocument patch, ClaimsPrincipal principal, CancellationToken ct) {
            if (!_resources.TryGetValue(id, out var current)) {
                return Task.FromResult<ScimResource?>(null);
            }

            var resource = ScimPatchProcessor.Apply(current, patch, Schemas);
            _resources[id] = resource;
            return Task.FromResult(ToResource(resource));
        }

        public Task<bool> DeleteAsync(string id, ClaimsPrincipal principal, CancellationToken ct) {
            return Task.FromResult(_resources.Remove(id));
        }

        private static ScimResource? ToResource(JsonObject? resource) {
            return resource is null ? null : JsonSerializer.Deserialize<JsonScimResource>(resource.ToJsonString(), ScimJson.Options);
        }
    }
}

[Trait("Layer", "Unit")]
public sealed class ScimBindingStoreShould
{
    [Fact]
    public async Task Reads_Use_Resource_Domain_And_External_Or_Entity_Identity() {
        var expected = new ScimProvisioningBinding { ResourceType = "User", ProvisioningDomain = "domain-a", ExternalId = "external-1", EntityUid = Guid.NewGuid() };
        var rows = new[] {
            expected,
            new ScimProvisioningBinding { ResourceType = "User", ProvisioningDomain = "domain-b", ExternalId = "external-1", EntityUid = Guid.NewGuid() },
        };
        var repository = new Mock<IRepository<ScimProvisioningBinding>>();
        repository.Setup(item => item.SingleOrDefaultAsync(
                It.IsAny<Func<IQueryable<ScimProvisioningBinding>, IQueryable<ScimProvisioningBinding>>>(),
                It.IsAny<CancellationToken>(), It.IsAny<QueryOptions>()))
            .Returns((Func<IQueryable<ScimProvisioningBinding>, IQueryable<ScimProvisioningBinding>> predicate, CancellationToken _, QueryOptions _) =>
                new ValueTask<ScimProvisioningBinding?>(predicate(rows.AsQueryable()).SingleOrDefault()));
        var store = new ScimBindingStore(repository.Object);

        Assert.Same(expected, await store.FindByExternalIdAsync("User", "domain-a", "external-1", CancellationToken.None));
        Assert.Same(expected, await store.FindByEntityUidAsync("User", expected.EntityUid, "domain-a", CancellationToken.None));
        Assert.Null(await store.FindByExternalIdAsync("User", "domain-c", "external-1", CancellationToken.None));
    }

    [Fact]
    public async Task Writes_Join_The_Caller_Transaction_Without_Committing() {
        var repository = new Mock<IRepository<ScimProvisioningBinding>>(MockBehavior.Strict);
        var transaction = new Mock<IUnitOfWork>(MockBehavior.Strict);
        var binding = new ScimProvisioningBinding { ResourceType = "User", ProvisioningDomain = "domain-a", ExternalId = "external-1", EntityUid = Guid.NewGuid() };
        repository.Setup(item => item.Join(transaction.Object));
        repository.Setup(item => item.AddAsync(binding, It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>())).ReturnsAsync(MutationResult.Applied);
        repository.Setup(item => item.RemoveAsync(binding, It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>())).ReturnsAsync(MutationResult.Applied);
        var store = new ScimBindingStore(repository.Object);

        await store.StageAsync(binding, transaction.Object, CancellationToken.None);
        await store.RemoveAsync(binding, transaction.Object, CancellationToken.None);

        repository.Verify(item => item.Join(transaction.Object), Times.Exactly(2));
        repository.Verify(item => item.AddAsync(binding, It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>()), Times.Once);
        repository.Verify(item => item.RemoveAsync(binding, It.IsAny<CancellationToken>(), It.IsAny<MutationOptions>()), Times.Once);
        transaction.Verify(item => item.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
