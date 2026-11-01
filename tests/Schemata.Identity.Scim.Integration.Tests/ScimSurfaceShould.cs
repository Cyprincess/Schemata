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
    private const string Users  = "/scim/v2/Users";
    private const string Groups = "/scim/v2/Groups";

    [Fact]
    public async Task Declare_Only_The_Implemented_Resources_And_Capabilities() {
        using var factory = new WebAppFactory();
        using var client  = CreateClient(factory);

        using (var response = await client.GetAsync("/scim/v2/ServiceProviderConfig")) {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(ScimConstants.MediaType, response.Content.Headers.ContentType?.MediaType);
            var root = await ReadJsonAsync(response);
            Assert.Equal(ScimConstants.ServiceProviderConfigSchema, root.GetProperty("schemas")[0].GetString());
            Assert.Equal("ServiceProviderConfig", root.GetProperty("meta").GetProperty("resourceType").GetString());
            Assert.False(root.GetProperty("meta").TryGetProperty("version", out _));

            foreach (var capability in new[] { "patch", "changePassword", "sort", "etag" }) {
                Assert.False(root.GetProperty(capability).GetProperty("supported").GetBoolean());
            }

            Assert.False(root.GetProperty("bulk").GetProperty("supported").GetBoolean());
            Assert.Equal(0, root.GetProperty("bulk").GetProperty("maxOperations").GetInt32());
            Assert.Equal(0, root.GetProperty("bulk").GetProperty("maxPayloadSize").GetInt32());
            Assert.False(root.GetProperty("filter").GetProperty("supported").GetBoolean());
            Assert.Equal(0, root.GetProperty("filter").GetProperty("maxResults").GetInt32());
        }

        using (var response = await client.GetAsync("/scim/v2/ResourceTypes")) {
            var root = await ReadJsonAsync(response);
            Assert.Equal(ScimConstants.ListResponseSchema, root.GetProperty("schemas")[0].GetString());

            var resources = root.GetProperty("Resources").EnumerateArray().ToList();
            Assert.Equal(2, resources.Count);
            Assert.Equal(2, root.GetProperty("totalResults").GetInt32());

            var user = resources.Single(resource => resource.GetProperty("name").GetString() == "User");
            Assert.Equal("/Users", user.GetProperty("endpoint").GetString());
            Assert.Equal(ScimConstants.UserSchema, user.GetProperty("schema").GetString());

            var group = resources.Single(resource => resource.GetProperty("name").GetString() == "Group");
            Assert.Equal("/Groups", group.GetProperty("endpoint").GetString());
            Assert.Equal(ScimConstants.GroupSchema, group.GetProperty("schema").GetString());
        }

        using (var response = await client.GetAsync("/scim/v2/ResourceTypes/Group")) {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("Group", (await ReadJsonAsync(response)).GetProperty("name").GetString());
        }

        using (var response = await client.GetAsync("/scim/v2/Schemas")) {
            var ids = (await ReadJsonAsync(response)).GetProperty("Resources")
                                                      .EnumerateArray()
                                                      .Select(resource => resource.GetProperty("id").GetString()!)
                                                      .ToList();
            Assert.Equal(5, ids.Count);
            Assert.Contains(ScimConstants.ServiceProviderConfigSchema, ids);
            Assert.Contains(ScimConstants.ResourceTypeSchema, ids);
            Assert.Contains(ScimConstants.SchemaSchema, ids);
            Assert.Contains(ScimConstants.UserSchema, ids);
            Assert.Contains(ScimConstants.GroupSchema, ids);
            Assert.DoesNotContain(ids, id => id.Contains("application", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(ids, id => id.Contains("enterprise", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public async Task Declare_User_And_Group_Attribute_Mutability() {
        using var factory = new WebAppFactory();
        using var client  = CreateClient(factory);

        using var userResponse = await client.GetAsync($"/scim/v2/Schemas/{Uri.EscapeDataString(ScimConstants.UserSchema)}");
        var userAttributes = (await ReadJsonAsync(userResponse)).GetProperty("attributes")
                                                                .EnumerateArray()
                                                                .ToDictionary(attribute => attribute.GetProperty("name").GetString()!);

        Assert.Equal(new[] { "active", "displayName", "groups", "password", "userName" },
                     userAttributes.Keys.OrderBy(name => name, StringComparer.Ordinal));

        var userName = userAttributes["userName"];
        Assert.True(userName.GetProperty("required").GetBoolean());
        Assert.Equal(ScimConstants.ReadWrite, userName.GetProperty("mutability").GetString());
        Assert.Equal(ScimConstants.UniquenessServer, userName.GetProperty("uniqueness").GetString());

        Assert.Equal(ScimConstants.ReadWrite, userAttributes["displayName"].GetProperty("mutability").GetString());
        Assert.Equal(ScimConstants.ReadWrite, userAttributes["active"].GetProperty("mutability").GetString());

        var password = userAttributes["password"];
        Assert.Equal(ScimConstants.WriteOnly, password.GetProperty("mutability").GetString());
        Assert.Equal(ScimConstants.Never, password.GetProperty("returned").GetString());

        var groups = userAttributes["groups"];
        Assert.True(groups.GetProperty("multiValued").GetBoolean());
        Assert.Equal(ScimConstants.ReadOnly, groups.GetProperty("mutability").GetString());
        var groupSubs = groups.GetProperty("subAttributes").EnumerateArray().ToList();
        Assert.Equal(4, groupSubs.Count);
        Assert.All(groupSubs, attribute => Assert.Equal(ScimConstants.ReadOnly, attribute.GetProperty("mutability").GetString()));
        Assert.Equal(new[] { "Group" }, groupSubs.Single(attribute => attribute.GetProperty("name").GetString() == "$ref")
                                                      .GetProperty("referenceTypes")
                                                      .EnumerateArray()
                                                      .Select(value => value.GetString()!));

        using var groupResponse = await client.GetAsync($"/scim/v2/Schemas/{Uri.EscapeDataString(ScimConstants.GroupSchema)}");
        var groupAttributes = (await ReadJsonAsync(groupResponse)).GetProperty("attributes")
                                                                  .EnumerateArray()
                                                                  .ToDictionary(attribute => attribute.GetProperty("name").GetString()!);

        Assert.Equal(new[] { "displayName", "members" },
                     groupAttributes.Keys.OrderBy(name => name, StringComparer.Ordinal));

        var displayName = groupAttributes["displayName"];
        Assert.True(displayName.GetProperty("required").GetBoolean());
        Assert.Equal(ScimConstants.ReadWrite, displayName.GetProperty("mutability").GetString());
        Assert.Equal(ScimConstants.UniquenessServer, displayName.GetProperty("uniqueness").GetString());

        var members = groupAttributes["members"];
        Assert.True(members.GetProperty("multiValued").GetBoolean());
        Assert.Equal(ScimConstants.ReadWrite, members.GetProperty("mutability").GetString());
        var memberSubs = members.GetProperty("subAttributes").EnumerateArray()
                                .ToDictionary(attribute => attribute.GetProperty("name").GetString()!);
        Assert.Equal(ScimConstants.Immutable, memberSubs["value"].GetProperty("mutability").GetString());
        Assert.Equal(ScimConstants.Immutable, memberSubs["$ref"].GetProperty("mutability").GetString());
        Assert.Equal(ScimConstants.Immutable, memberSubs["type"].GetProperty("mutability").GetString());
        Assert.Equal(ScimConstants.ReadOnly, memberSubs["display"].GetProperty("mutability").GetString());
        Assert.Equal(new[] { "User" }, memberSubs["type"].GetProperty("canonicalValues")
                                                         .EnumerateArray()
                                                         .Select(value => value.GetString()!));
        Assert.Equal(new[] { "User" }, memberSubs["$ref"].GetProperty("referenceTypes")
                                                         .EnumerateArray()
                                                         .Select(value => value.GetString()!));
    }

    [Fact]
    public async Task Serve_The_User_Operation_Matrix() {
        using var factory = new WebAppFactory();
        using var client  = CreateClient(factory);

        using var created = await client.PostAsync(Users, UserContent("Matrix-User", "Matrix User"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(ScimConstants.MediaType, created.Content.Headers.ContentType?.MediaType);
        var body = await ReadJsonAsync(created);
        var id   = body.GetProperty("id").GetString()!;
        Assert.Equal(ScimConstants.UserSchema, body.GetProperty("schemas")[0].GetString());
        Assert.Equal("User", body.GetProperty("meta").GetProperty("resourceType").GetString());
        var location = body.GetProperty("meta").GetProperty("location").GetString();
        Assert.EndsWith($"{Users}/{id}", location);
        Assert.Equal(location, created.Headers.Location?.ToString());
        Assert.Equal(location, created.Content.Headers.ContentLocation?.ToString());

        using (var single = await client.GetAsync($"{Users}/{id}")) {
            Assert.Equal(HttpStatusCode.OK, single.StatusCode);
            Assert.Equal(ScimConstants.MediaType, single.Content.Headers.ContentType?.MediaType);
            Assert.Equal(location, single.Content.Headers.ContentLocation?.ToString());
            Assert.Equal(id, (await ReadJsonAsync(single)).GetProperty("id").GetString());
        }

        using (var list = await client.GetAsync(Users)) {
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
            Assert.Equal(ScimConstants.MediaType, list.Content.Headers.ContentType?.MediaType);
            var root = await ReadJsonAsync(list);
            Assert.Equal(ScimConstants.ListResponseSchema, root.GetProperty("schemas")[0].GetString());
            Assert.Equal(1, root.GetProperty("totalResults").GetInt32());
            Assert.Equal(1, root.GetProperty("startIndex").GetInt32());
            Assert.Equal(1, root.GetProperty("itemsPerPage").GetInt32());
            Assert.Equal(id, root.GetProperty("Resources")[0].GetProperty("id").GetString());
        }

        using (var replaced = await client.PutAsync($"{Users}/{id}", UserContent("Matrix-User", "Matrix Renamed"))) {
            Assert.Equal(HttpStatusCode.OK, replaced.StatusCode);
            Assert.Equal(ScimConstants.MediaType, replaced.Content.Headers.ContentType?.MediaType);
            var root = await ReadJsonAsync(replaced);
            Assert.Equal(id, root.GetProperty("id").GetString());
            Assert.Equal("Matrix Renamed", root.GetProperty("displayName").GetString());
        }

        using (var postItem = await client.PostAsync($"{Users}/{id}", UserContent("Matrix-User"))) {
            Assert.Equal(HttpStatusCode.MethodNotAllowed, postItem.StatusCode);
            await AssertScimErrorAsync(postItem, "405");
        }

        using (var putCollection = await client.PutAsync(Users, UserContent("Matrix-User"))) {
            Assert.Equal(HttpStatusCode.MethodNotAllowed, putCollection.StatusCode);
            await AssertScimErrorAsync(putCollection, "405");
        }

        using (var deleteCollection = await client.DeleteAsync(Users)) {
            Assert.Equal(HttpStatusCode.MethodNotAllowed, deleteCollection.StatusCode);
            await AssertScimErrorAsync(deleteCollection, "405");
        }

        using (var patchCollection = await client.PatchAsync(Users, JsonContent(new Dictionary<string, object?>()))) {
            Assert.Equal(HttpStatusCode.MethodNotAllowed, patchCollection.StatusCode);
            await AssertScimErrorAsync(patchCollection, "405");
        }

        using (var patchItem = await client.PatchAsync($"{Users}/{id}", JsonContent(new Dictionary<string, object?>()))) {
            Assert.Equal(HttpStatusCode.NotImplemented, patchItem.StatusCode);
            await AssertScimErrorAsync(patchItem, "501");
        }

        using (var deleted = await client.DeleteAsync($"{Users}/{id}")) {
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        }

        using (var missing = await client.GetAsync($"{Users}/{id}")) {
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            await AssertScimErrorAsync(missing, "404");
        }
    }

    [Fact]
    public async Task Serve_The_Group_Operation_Matrix() {
        using var factory = new WebAppFactory();
        using var client  = CreateClient(factory);
        var       user    = await CreateUserAsync(client, "Matrix-Member", "Matrix Member");
        var       userId  = user.GetProperty("id").GetString()!;

        using var created = await client.PostAsync(Groups, GroupContent("Matrix-Group", Member(userId)));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(ScimConstants.MediaType, created.Content.Headers.ContentType?.MediaType);
        var body = await ReadJsonAsync(created);
        var id   = body.GetProperty("id").GetString()!;
        Assert.Equal(ScimConstants.GroupSchema, body.GetProperty("schemas")[0].GetString());
        Assert.Equal("Group", body.GetProperty("meta").GetProperty("resourceType").GetString());
        var location = body.GetProperty("meta").GetProperty("location").GetString();
        Assert.EndsWith($"{Groups}/{id}", location);
        Assert.Equal(location, created.Headers.Location?.ToString());
        Assert.Equal(location, created.Content.Headers.ContentLocation?.ToString());

        var member = body.GetProperty("members")[0];
        Assert.Equal(userId, member.GetProperty("value").GetString());
        Assert.Equal("User", member.GetProperty("type").GetString());
        Assert.EndsWith($"{Users}/{userId}", member.GetProperty("$ref").GetString());

        using (var single = await client.GetAsync($"{Groups}/{id}")) {
            Assert.Equal(HttpStatusCode.OK, single.StatusCode);
            Assert.Equal(ScimConstants.MediaType, single.Content.Headers.ContentType?.MediaType);
            Assert.Equal(location, single.Content.Headers.ContentLocation?.ToString());
            Assert.Equal(1, (await ReadJsonAsync(single)).GetProperty("members").GetArrayLength());
        }

        using (var list = await client.GetAsync(Groups)) {
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
            var root = await ReadJsonAsync(list);
            Assert.Equal(ScimConstants.ListResponseSchema, root.GetProperty("schemas")[0].GetString());
            Assert.Equal(1, root.GetProperty("totalResults").GetInt32());
            Assert.Equal(1, root.GetProperty("startIndex").GetInt32());
            Assert.Equal(1, root.GetProperty("itemsPerPage").GetInt32());
            Assert.Equal(id, root.GetProperty("Resources")[0].GetProperty("id").GetString());
        }

        using (var replaced = await client.PutAsync($"{Groups}/{id}", GroupContent("Matrix-Group-Renamed"))) {
            Assert.Equal(HttpStatusCode.OK, replaced.StatusCode);
            var root = await ReadJsonAsync(replaced);
            Assert.Equal(id, root.GetProperty("id").GetString());
            Assert.Equal("Matrix-Group-Renamed", root.GetProperty("displayName").GetString());
            Assert.Equal(0, root.GetProperty("members").GetArrayLength());
        }

        using (var postItem = await client.PostAsync($"{Groups}/{id}", GroupContent("Matrix-Group"))) {
            Assert.Equal(HttpStatusCode.MethodNotAllowed, postItem.StatusCode);
            await AssertScimErrorAsync(postItem, "405");
        }

        using (var putCollection = await client.PutAsync(Groups, GroupContent("Matrix-Group"))) {
            Assert.Equal(HttpStatusCode.MethodNotAllowed, putCollection.StatusCode);
            await AssertScimErrorAsync(putCollection, "405");
        }

        using (var deleteCollection = await client.DeleteAsync(Groups)) {
            Assert.Equal(HttpStatusCode.MethodNotAllowed, deleteCollection.StatusCode);
            await AssertScimErrorAsync(deleteCollection, "405");
        }

        using (var patchItem = await client.PatchAsync($"{Groups}/{id}", JsonContent(new Dictionary<string, object?>()))) {
            Assert.Equal(HttpStatusCode.NotImplemented, patchItem.StatusCode);
            await AssertScimErrorAsync(patchItem, "501");
        }

        using (var deleted = await client.DeleteAsync($"{Groups}/{id}")) {
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        }

        using (var missing = await client.GetAsync($"{Groups}/{id}")) {
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            await AssertScimErrorAsync(missing, "404");
        }
    }

    [Fact]
    public async Task Expose_No_Endpoints_For_Unsupported_Operations() {
        using var factory = new WebAppFactory();
        using var client  = CreateClient(factory);

        foreach (var path in new[] { "/scim/v2/Me", "/scim/v2/Bulk", "/scim/v2/Applications", $"{Users}/.search" }) {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            await AssertScimErrorAsync(response, "404");
        }

        using (var search = await client.PostAsync($"{Users}/.search", JsonContent(new Dictionary<string, object?>()))) {
            Assert.Equal(HttpStatusCode.MethodNotAllowed, search.StatusCode);
            await AssertScimErrorAsync(search, "405");
        }

        using (var bulk = await client.PostAsync("/scim/v2/Bulk", JsonContent(new Dictionary<string, object?>()))) {
            Assert.Equal(HttpStatusCode.NotFound, bulk.StatusCode);
            await AssertScimErrorAsync(bulk, "404");
        }
    }

    [Fact]
    public async Task Ignore_Unsupported_Collection_Parameters_And_Reject_Filters() {
        using var factory = new WebAppFactory();
        using var client  = CreateClient(factory);
        var       user    = await CreateUserAsync(client, "Parameter-User", "Parameter Display");
        var       id      = user.GetProperty("id").GetString()!;

        using (var filtered = await client.GetAsync($"{Users}?filter={Uri.EscapeDataString("userName eq \"Parameter-User\"")}")) {
            Assert.Equal(HttpStatusCode.BadRequest, filtered.StatusCode);
            await AssertScimErrorAsync(filtered, "400", ScimConstants.InvalidFilter);
        }

        using (var projected = await client.GetAsync($"{Users}/{id}?attributes=userName&excludedAttributes=displayName")) {
            Assert.Equal(HttpStatusCode.OK, projected.StatusCode);
            var root = await ReadJsonAsync(projected);
            Assert.True(root.TryGetProperty("id", out _));
            Assert.True(root.TryGetProperty("schemas", out _));
            Assert.True(root.TryGetProperty("meta", out _));
            Assert.True(root.TryGetProperty("userName", out _));
            Assert.True(root.TryGetProperty("displayName", out _));
        }

        using (var listed = await client.GetAsync($"{Users}?attributes=userName&excludedAttributes=displayName")) {
            Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
            var resource = (await ReadJsonAsync(listed)).GetProperty("Resources")[0];
            Assert.True(resource.TryGetProperty("id", out _));
            Assert.True(resource.TryGetProperty("meta", out _));
            Assert.True(resource.TryGetProperty("displayName", out _));
        }

        using (var sorted = await client.GetAsync($"{Users}?sortBy=userName&sortOrder=descending")) {
            Assert.Equal(HttpStatusCode.OK, sorted.StatusCode);
            var root = await ReadJsonAsync(sorted);
            Assert.Equal(ScimConstants.ListResponseSchema, root.GetProperty("schemas")[0].GetString());
            Assert.Equal(1, root.GetProperty("totalResults").GetInt32());
        }
    }

    [Fact]
    public async Task Convert_Unsupported_Media_Types_To_Scim_Errors() {
        using var factory  = new WebAppFactory();
        using var client   = CreateClient(factory);
        using var response = await client.PostAsync(Users,
                                                     new StringContent("{\"userName\":\"Media-User\"}", Encoding.UTF8, "text/plain"));

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        var root = await ReadJsonAsync(response);
        Assert.False(root.TryGetProperty("title", out _));
        Assert.False(root.TryGetProperty("type", out _));
        await AssertScimErrorAsync(response, "415", ScimConstants.InvalidSyntax);
    }

    [Fact]
    public async Task Emit_No_Entity_Tags_And_Ignore_If_Match() {
        using var factory = new WebAppFactory();
        using var client  = CreateClient(factory);
        var       user    = await CreateUserAsync(client, "Etag-User");
        var       id      = user.GetProperty("id").GetString()!;

        using (var single = await client.GetAsync($"{Users}/{id}")) {
            Assert.False(single.Headers.Contains("ETag"));
            Assert.False((await ReadJsonAsync(single)).GetProperty("meta").TryGetProperty("version", out _));
        }

        using var request = new HttpRequestMessage(HttpMethod.Put, $"{Users}/{id}") {
            Content = UserContent("Etag-User", "Etag Updated"),
        };
        request.Headers.TryAddWithoutValidation("If-Match", "\"stale-tag\"");

        using var replaced = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, replaced.StatusCode);
        Assert.Equal("Etag Updated", (await ReadJsonAsync(replaced)).GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task Require_Authentication_For_Resources_Only() {
        using var factory   = new WebAppFactory();
        using var anonymous = CreateClient(factory, authenticated: false);

        foreach (var path in new[] { "/scim/v2/ServiceProviderConfig", "/scim/v2/ResourceTypes", "/scim/v2/Schemas" }) {
            using var response = await anonymous.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(ScimConstants.MediaType, response.Content.Headers.ContentType?.MediaType);
        }

        using (var users = await anonymous.GetAsync(Users)) {
            Assert.Equal(HttpStatusCode.Unauthorized, users.StatusCode);
            await AssertScimErrorAsync(users, "401");
        }

        using (var groups = await anonymous.GetAsync(Groups)) {
            Assert.Equal(HttpStatusCode.Unauthorized, groups.StatusCode);
            await AssertScimErrorAsync(groups, "401");
        }
    }

    private static HttpClient CreateClient(WebAppFactory factory, bool authenticated = true) {
        var client = factory.CreateClient();
        if (authenticated) {
            client.DefaultRequestHeaders.Add("X-Test-Scim", "1");
        }

        return client;
    }

    private static async Task<JsonElement> CreateUserAsync(
        HttpClient client,
        string     userName,
        string?    displayName = null
    ) {
        using var response = await client.PostAsync(Users, UserContent(userName, displayName));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return await ReadJsonAsync(response);
    }

    private static StringContent UserContent(string userName, string? displayName = null) {
        var payload = new Dictionary<string, object?> { ["userName"] = userName };
        if (displayName is not null) {
            payload["displayName"] = displayName;
        }

        return JsonContent(payload);
    }

    private static StringContent GroupContent(string displayName, params object[] members) {
        var payload = new Dictionary<string, object?> { ["displayName"] = displayName };
        if (members.Length > 0) {
            payload["members"] = members;
        }

        return JsonContent(payload);
    }

    private static Dictionary<string, object?> Member(string userId) {
        return new() {
            ["value"] = userId,
            ["$ref"]  = $"{Users}/{userId}",
            ["type"]  = "User",
        };
    }

    private static StringContent JsonContent(object payload) {
        return new(JsonSerializer.Serialize(payload), Encoding.UTF8, ScimConstants.MediaType);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return document.RootElement.Clone();
    }

    private static async Task AssertScimErrorAsync(HttpResponseMessage response, string status, string? scimType = null) {
        Assert.Equal(ScimConstants.MediaType, response.Content.Headers.ContentType?.MediaType);
        var root = await ReadJsonAsync(response);
        Assert.Equal(ScimConstants.ErrorSchema, root.GetProperty("schemas")[0].GetString());
        Assert.Equal(JsonValueKind.String, root.GetProperty("status").ValueKind);
        Assert.Equal(status, root.GetProperty("status").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("detail").GetString()));
        if (scimType is null) {
            Assert.False(root.TryGetProperty("scimType", out _));
        } else {
            Assert.Equal(scimType, root.GetProperty("scimType").GetString());
        }

        Assert.False(root.TryGetProperty("error", out _));
    }
}
