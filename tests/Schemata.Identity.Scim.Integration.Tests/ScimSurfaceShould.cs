using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Schemata.Identity.Scim.Integration.Tests.Fixtures;
using Schemata.Scim;
using Xunit;

namespace Schemata.Identity.Scim.Integration.Tests;

[Trait("Layer", "Integration")]
public sealed class ScimSurfaceShould
{
    [Fact]
    public async Task Discovery_Advertises_The_Identity_Protocol_Profile() {
        using var factory = new WebAppFactory();
        using var client = CreateClient(factory);

        using (var response = await client.GetAsync("/scim/v2/ServiceProviderConfig")) {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var root = await ReadJsonAsync(response);
            Assert.True(root.GetProperty("patch").GetProperty("supported").GetBoolean());
            Assert.True(root.GetProperty("filter").GetProperty("supported").GetBoolean());
            Assert.Equal(200, root.GetProperty("filter").GetProperty("maxResults").GetInt32());
            foreach (var capability in new[] { "changePassword", "sort", "etag", "bulk" }) {
                Assert.False(root.GetProperty(capability).GetProperty("supported").GetBoolean());
            }
        }

        using (var response = await client.GetAsync("/scim/v2/ResourceTypes")) {
            var root = await ReadJsonAsync(response);
            var resources = root.GetProperty("Resources").EnumerateArray().ToList();
            Assert.Equal(2, resources.Count);
            Assert.Contains(resources, resource => resource.GetProperty("name").GetString() == "User"
                && resource.GetProperty("endpoint").GetString() == "/Users"
                && resource.GetProperty("schema").GetString() == ScimConstants.UserSchema);
            Assert.Contains(resources, resource => resource.GetProperty("name").GetString() == "Group"
                && resource.GetProperty("endpoint").GetString() == "/Groups"
                && resource.GetProperty("schema").GetString() == ScimConstants.GroupSchema);
        }

        using (var response = await client.GetAsync("/scim/v2/Schemas")) {
            var ids = (await ReadJsonAsync(response)).GetProperty("Resources").EnumerateArray()
                                                      .Select(resource => resource.GetProperty("id").GetString()).ToList();
            Assert.Equal(5, ids.Count);
            Assert.Contains(ScimConstants.UserSchema, ids);
            Assert.Contains(ScimConstants.GroupSchema, ids);
            Assert.DoesNotContain(ScimConstants.EnterpriseUserSchema, ids);
        }
    }

    [Fact]
    public async Task User_Schema_Excludes_Secrets_And_Marks_Groups_ReadOnly() {
        using var factory = new WebAppFactory();
        using var client = CreateClient(factory);

        using var response = await client.GetAsync($"/scim/v2/Schemas/{Uri.EscapeDataString(ScimConstants.UserSchema)}");
        var attributes = (await ReadJsonAsync(response)).GetProperty("attributes").EnumerateArray()
                                                         .ToDictionary(attribute => attribute.GetProperty("name").GetString()!);
        Assert.Equal(new[] { "active", "displayName", "externalId", "groups", "id", "meta", "schemas", "userName" },
            attributes.Keys.OrderBy(name => name, StringComparer.Ordinal));
        Assert.False(attributes.ContainsKey("password"));
        Assert.True(attributes["userName"].GetProperty("required").GetBoolean());
        Assert.Equal(ScimConstants.UniquenessServer, attributes["userName"].GetProperty("uniqueness").GetString());
        Assert.Equal(ScimConstants.ReadWrite, attributes["active"].GetProperty("mutability").GetString());
        Assert.Equal(ScimConstants.ReadOnly, attributes["groups"].GetProperty("mutability").GetString());
        Assert.All(attributes["groups"].GetProperty("subAttributes").EnumerateArray(),
            attribute => Assert.Equal(ScimConstants.ReadOnly, attribute.GetProperty("mutability").GetString()));
    }

    [Fact]
    public async Task Shared_Unsupported_Routes_And_Search_Use_The_Protocol_Contract() {
        using var factory = new WebAppFactory();
        using var client = CreateClient(factory);

        foreach (var path in new[] { "/scim/v2/Me", "/scim/v2/Bulk" }) {
            using var response = await client.PostAsync(path, JsonContent(new Dictionary<string, object?>()));
            Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
            AssertScimError(await ReadJsonAsync(response), "501");
        }

        using (var groups = await client.GetAsync("/scim/v2/Groups")) {
            Assert.Equal(HttpStatusCode.OK, groups.StatusCode);
            Assert.Equal(0, (await ReadJsonAsync(groups)).GetProperty("totalResults").GetInt32());
        }

        using var search = await client.PostAsync("/scim/v2/Users/.search", JsonContent(new Dictionary<string, object?> {
            ["schemas"] = new[] { ScimConstants.SearchRequestSchema },
        }));
        Assert.Equal(HttpStatusCode.OK, search.StatusCode);
        Assert.Equal(0, (await ReadJsonAsync(search)).GetProperty("totalResults").GetInt32());
    }

    [Fact]
    public async Task Authentication_And_Write_Authorization_Are_Independent() {
        using (var factory = new WebAppFactory()) {
            using var anonymous = CreateClient(factory, authenticated: false);
            using var discovery = await anonymous.GetAsync("/scim/v2/ResourceTypes");
            Assert.Equal(HttpStatusCode.OK, discovery.StatusCode);
            using var users = await anonymous.GetAsync("/scim/v2/Users");
            Assert.Equal(HttpStatusCode.Unauthorized, users.StatusCode);
        }

        using (var factory = new WebAppFactory("Denied")) {
            using var client = CreateClient(factory);
            using var read = await client.GetAsync("/scim/v2/Users");
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            using var write = await client.PostAsync("/scim/v2/Users", JsonContent(new Dictionary<string, object?> {
                ["userName"] = "Denied",
            }));
            Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
            AssertScimError(await ReadJsonAsync(write), "403");
        }
    }

    [Fact]
    public async Task Custom_Role_Selection_Registers_The_Group_Handler() {
        using var factory = new WebAppFactory("CustomRole");
        using var client = CreateClient(factory);
        using var created = await client.PostAsync("/scim/v2/Users", JsonContent(new Dictionary<string, object?> {
            ["userName"] = "Custom-Role-Host",
        }));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var group = await client.PostAsync("/scim/v2/Groups", JsonContent(new Dictionary<string, object?> {
            ["displayName"] = "Custom Role Group",
        }));
        Assert.Equal(HttpStatusCode.Created, group.StatusCode);
        using var groups = await client.GetAsync("/scim/v2/Groups");
        Assert.Equal(1, (await ReadJsonAsync(groups)).GetProperty("totalResults").GetInt32());
        using var schema = await client.GetAsync($"/scim/v2/Schemas/{Uri.EscapeDataString(ScimConstants.GroupSchema)}");
        Assert.Equal(HttpStatusCode.OK, schema.StatusCode);
    }

    private static HttpClient CreateClient(WebAppFactory factory, bool authenticated = true) {
        var client = factory.CreateClient();
        if (authenticated) {
            client.DefaultRequestHeaders.Add("X-Test-Scim", "1");
        }
        return client;
    }

    private static StringContent JsonContent(object payload) {
        return new(JsonSerializer.Serialize(payload), Encoding.UTF8, ScimConstants.MediaType);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static void AssertScimError(JsonElement root, string status) {
        Assert.Equal(ScimConstants.ErrorSchema, root.GetProperty("schemas")[0].GetString());
        Assert.Equal(status, root.GetProperty("status").GetString());
    }
}
