using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Identity.Scim.Integration.Tests.Fixtures;
using Schemata.Identity.Scim.Services;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Managers;
using Schemata.Scim;
using Xunit;

namespace Schemata.Identity.Scim.Integration.Tests;

[Trait("Layer", "Integration")]
public sealed class ScimUsersShould
{
    private const string Users = "/scim/v2/Users";

    [Fact]
    public async Task Create_Get_List_Replace_And_Delete_A_User() {
        using var factory = new WebAppFactory();
        using var client  = CreateClient(factory);

        using var created = await client.PostAsync(Users, UserContent("Alice", "Alice Example", true));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(ScimConstants.MediaType, created.Content.Headers.ContentType?.MediaType);
        var body = await ReadJsonAsync(created);
        var id   = body.GetProperty("id").GetString();
        Assert.NotNull(id);
        Assert.True(Guid.TryParse(id, out _));
        Assert.Equal(ScimConstants.UserSchema, body.GetProperty("schemas")[0].GetString());
        Assert.Equal("Alice", body.GetProperty("userName").GetString());
        Assert.Equal("Alice Example", body.GetProperty("displayName").GetString());
        Assert.True(body.GetProperty("active").GetBoolean());
        Assert.Equal("User", body.GetProperty("meta").GetProperty("resourceType").GetString());
        Assert.EndsWith($"{Users}/{id}", body.GetProperty("meta").GetProperty("location").GetString());
        Assert.Equal(0, body.GetProperty("groups").GetArrayLength());
        Assert.Equal(body.GetProperty("meta").GetProperty("location").GetString(), created.Headers.Location?.ToString());

        using (var single = await client.GetAsync($"{Users}/{id}")) {
            Assert.Equal(HttpStatusCode.OK, single.StatusCode);
            var root = await ReadJsonAsync(single);
            Assert.Equal(id, root.GetProperty("id").GetString());
            Assert.Equal("Alice", root.GetProperty("userName").GetString());
        }

        using (var list = await client.GetAsync(Users)) {
            var root = await ReadJsonAsync(list);
            Assert.Equal(ScimConstants.ListResponseSchema, root.GetProperty("schemas")[0].GetString());
            Assert.Equal(1, root.GetProperty("totalResults").GetInt32());
            Assert.Equal(1, root.GetProperty("startIndex").GetInt32());
            Assert.Equal(1, root.GetProperty("itemsPerPage").GetInt32());
            Assert.Equal(id, root.GetProperty("Resources")[0].GetProperty("id").GetString());
        }

        using (var replaced = await client.PutAsync($"{Users}/{id}", UserContent("Alice-Renamed", "Alice Renamed", true))) {
            Assert.Equal(HttpStatusCode.OK, replaced.StatusCode);
            var root = await ReadJsonAsync(replaced);
            Assert.Equal(id, root.GetProperty("id").GetString());
            Assert.Equal("Alice-Renamed", root.GetProperty("userName").GetString());
            Assert.Equal("Alice Renamed", root.GetProperty("displayName").GetString());
        }

        using (var single = await client.GetAsync($"{Users}/{id}")) {
            var root = await ReadJsonAsync(single);
            Assert.Equal("Alice-Renamed", root.GetProperty("userName").GetString());
            Assert.Equal("Alice Renamed", root.GetProperty("displayName").GetString());
        }

        using (var deleted = await client.DeleteAsync($"{Users}/{id}")) {
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        }

        using (var missing = await client.GetAsync($"{Users}/{id}")) {
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            AssertScimError(await ReadJsonAsync(missing), "404");
        }

        using (var list = await client.GetAsync(Users)) {
            var root = await ReadJsonAsync(list);
            Assert.Equal(0, root.GetProperty("totalResults").GetInt32());
            Assert.Equal(0, root.GetProperty("Resources").GetArrayLength());
        }
    }

    [Fact]
    public async Task Replace_Does_Not_Create_Missing_Users() {
        using var factory = new WebAppFactory();
        using var client  = CreateClient(factory);
        var       id      = Guid.NewGuid().ToString();

        using var response = await client.PutAsync($"{Users}/{id}", UserContent("Ghost"));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        AssertScimError(await ReadJsonAsync(response), "404");

        using var list = await client.GetAsync(Users);
        Assert.Equal(0, (await ReadJsonAsync(list)).GetProperty("totalResults").GetInt32());
    }

    [Fact]
    public async Task Page_List_Responses_With_Clamped_Start_Index_And_Count() {
        using var factory = new WebAppFactory();
        using var client  = CreateClient(factory);
        foreach (var name in new[] { "Page-A", "Page-B", "Page-C" }) {
            await CreateUserAsync(client, name);
        }

        using (var response = await client.GetAsync($"{Users}?startIndex=2&count=1")) {
            var root = await ReadJsonAsync(response);
            Assert.Equal(3, root.GetProperty("totalResults").GetInt32());
            Assert.Equal(2, root.GetProperty("startIndex").GetInt32());
            Assert.Equal(1, root.GetProperty("itemsPerPage").GetInt32());
            Assert.Equal(1, root.GetProperty("Resources").GetArrayLength());
        }

        using (var response = await client.GetAsync($"{Users}?startIndex=0&count=10")) {
            var root = await ReadJsonAsync(response);
            Assert.Equal(1, root.GetProperty("startIndex").GetInt32());
            Assert.Equal(3, root.GetProperty("itemsPerPage").GetInt32());
            Assert.Equal(3, root.GetProperty("Resources").GetArrayLength());
        }

        using (var response = await client.GetAsync($"{Users}?startIndex=1&count=-5")) {
            var root = await ReadJsonAsync(response);
            Assert.Equal(3, root.GetProperty("totalResults").GetInt32());
            Assert.Equal(0, root.GetProperty("itemsPerPage").GetInt32());
            Assert.Equal(0, root.GetProperty("Resources").GetArrayLength());
        }

        using (var response = await client.GetAsync($"{Users}?count=2")) {
            var root      = await ReadJsonAsync(response);
            var resources = root.GetProperty("Resources");
            Assert.Equal(2, root.GetProperty("itemsPerPage").GetInt32());
            Assert.Equal(2, resources.GetArrayLength());
        }

        using (var response = await client.GetAsync($"{Users}?startIndex=99")) {
            var root = await ReadJsonAsync(response);
            Assert.Equal(99, root.GetProperty("startIndex").GetInt32());
            Assert.Equal(0, root.GetProperty("itemsPerPage").GetInt32());
            Assert.Equal(0, root.GetProperty("Resources").GetArrayLength());
        }
    }

    [Fact]
    public async Task Reject_A_Filter_Expression() {
        using var factory = new WebAppFactory();
        using var client  = CreateClient(factory);
        await CreateUserAsync(client, "Filtered");

        using var response = await client.GetAsync($"{Users}?filter={Uri.EscapeDataString("userName eq \"Filtered\"")}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertScimError(await ReadJsonAsync(response), "400", ScimConstants.InvalidFilter);
    }

    [Fact]
    public async Task Cap_The_Page_Size_With_The_Configured_Maximum() {
        using var factory = new WebAppFactory("Limited");
        using var client  = CreateClient(factory);
        foreach (var name in new[] { "Cap-A", "Cap-B", "Cap-C" }) {
            await CreateUserAsync(client, name);
        }

        using var response = await client.GetAsync($"{Users}?count=10");
        var       root     = await ReadJsonAsync(response);

        Assert.Equal(3, root.GetProperty("totalResults").GetInt32());
        Assert.Equal(2, root.GetProperty("itemsPerPage").GetInt32());
        Assert.Equal(2, root.GetProperty("Resources").GetArrayLength());
    }

    [Fact]
    public async Task Report_Patch_As_Not_Implemented() {
        using var factory = new WebAppFactory();
        using var client  = CreateClient(factory);
        var       body    = await CreateUserAsync(client, "Patch-Target");
        var       id      = body.GetProperty("id").GetString();

        using var response = await client.PatchAsync(
            $"{Users}/{id}",
            new StringContent($"{{\"schemas\":[\"{ScimConstants.UserSchema}\"],\"Operations\":[]}}", Encoding.UTF8, ScimConstants.MediaType));

        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
        AssertScimError(await ReadJsonAsync(response), "501");
    }

    [Fact]
    public async Task Ignore_Read_Only_Input_And_Never_Return_The_Password() {
        using var factory = new WebAppFactory();
        using var client  = CreateClient(factory);

        using var created = await client.PostAsync(
            Users,
            UserContent("Dana", "Dana Example", true, "Valid-Pass123!", "client-id",
                        groups: new[] { new Dictionary<string, string> { ["value"] = "g-1" } },
                        meta: new Dictionary<string, object>(),
                        schemas: [ScimConstants.UserSchema]));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await ReadJsonAsync(created);
        var id   = body.GetProperty("id").GetString();
        Assert.NotEqual("client-id", id);
        Assert.True(Guid.TryParse(id, out _));
        Assert.Equal(0, body.GetProperty("groups").GetArrayLength());
        Assert.False(body.TryGetProperty("password", out _));
        Assert.DoesNotContain("Valid-Pass123!", await created.Content.ReadAsStringAsync());

        using (var replaced = await client.PutAsync($"{Users}/{id}", UserContent("Dana", null, true, "Another-Pass123!", "other-id",
                                                                                  groups: new[] { new Dictionary<string, string> { ["value"] = "g-2" } },
                                                                                  schemas: [ScimConstants.UserSchema]))) {
            Assert.Equal(HttpStatusCode.OK, replaced.StatusCode);
            var root = await ReadJsonAsync(replaced);
            Assert.Equal(id, root.GetProperty("id").GetString());
            Assert.Equal(0, root.GetProperty("groups").GetArrayLength());
            Assert.False(root.TryGetProperty("password", out _));
            Assert.False(root.TryGetProperty("displayName", out _));
            Assert.DoesNotContain("Another-Pass123!", await replaced.Content.ReadAsStringAsync());
        }

        using var scope = factory.Services.CreateScope();
        var       users = scope.ServiceProvider.GetRequiredService<SchemataUserManager<SchemataUser>>();
        var       user  = await users.FindByNameAsync("Dana");
        Assert.NotNull(user);
        Assert.True(await users.CheckPasswordAsync(user!, "Another-Pass123!"));
        Assert.False(await users.CheckPasswordAsync(user!, "Valid-Pass123!"));
    }

    [Fact]
    public async Task Keep_The_Server_Id_Immutable_Across_Rename_And_Rotate_The_Security_Stamp() {
        using var factory = new WebAppFactory();
        using var client  = CreateClient(factory);
        var       body    = await CreateUserAsync(client, "Eve");
        var       id      = body.GetProperty("id").GetString()!;
        string?   stamp;
        using (var scope = factory.Services.CreateScope()) {
            stamp = (await FindAsync(scope, "Eve"))!.SecurityStamp;
        }

        using (var replaced = await client.PutAsync($"{Users}/{id}", UserContent("Eve-Renamed", "Eve", true, id: "forged"))) {
            Assert.Equal(HttpStatusCode.OK, replaced.StatusCode);
            Assert.Equal(id, (await ReadJsonAsync(replaced)).GetProperty("id").GetString());
        }

        using (var scope = factory.Services.CreateScope()) {
            Assert.Null(await FindAsync(scope, "Eve"));
            var renamed = await FindAsync(scope, "Eve-Renamed");
            Assert.NotNull(renamed);
            Assert.NotEqual(stamp, renamed!.SecurityStamp);
        }

        using (var original = await client.GetAsync($"{Users}/{id}")) {
            Assert.Equal(HttpStatusCode.OK, original.StatusCode);
            Assert.Equal("Eve-Renamed", (await ReadJsonAsync(original)).GetProperty("userName").GetString());
        }

        using var forged = await client.GetAsync($"{Users}/forged");
        Assert.Equal(HttpStatusCode.NotFound, forged.StatusCode);
    }

    [Fact]
    public async Task Reject_Empty_And_Case_Insensitively_Duplicate_User_Names() {
        using var factory = new WebAppFactory();
        using var client  = CreateClient(factory);

        using (var empty = await client.PostAsync(Users, UserContent("   "))) {
            Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
            AssertScimError(await ReadJsonAsync(empty), "400", ScimConstants.InvalidValue);
        }

        await CreateUserAsync(client, "Frank");

        foreach (var duplicate in new[] { "frank", "FRANK", "Frank" }) {
            using var response = await client.PostAsync(Users, UserContent(duplicate));
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            AssertScimError(await ReadJsonAsync(response), "409", ScimConstants.Uniqueness);
        }

        var grace = await CreateUserAsync(client, "Grace");
        using (var response = await client.PutAsync($"{Users}/{grace.GetProperty("id").GetString()}",
                                                    UserContent("frank", "Grace", true))) {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            AssertScimError(await ReadJsonAsync(response), "409", ScimConstants.Uniqueness);
        }

        using var list = await client.GetAsync(Users);
        Assert.Equal(2, (await ReadJsonAsync(list)).GetProperty("totalResults").GetInt32());
    }

    [Fact]
    public async Task Map_Active_To_The_Administrative_Sentinel() {
        using var factory = new WebAppFactory();
        using var client  = CreateClient(factory);

        var body = await CreateUserAsync(client, "Henry", active: false);
        var id   = body.GetProperty("id").GetString()!;
        Assert.False(body.GetProperty("active").GetBoolean());

        string? stamp;
        using (var scope = factory.Services.CreateScope()) {
            var user = await FindAsync(scope, "Henry");
            Assert.Equal(DateTimeOffset.MaxValue, user!.LockoutEnd);
            Assert.True(await scope.ServiceProvider.GetRequiredService<SchemataUserManager<SchemataUser>>().IsLockedOutAsync(user));
            stamp = user.SecurityStamp;
        }

        using (var replaced = await client.PutAsync($"{Users}/{id}", UserContent("Henry", "Henry", true))) {
            Assert.Equal(HttpStatusCode.OK, replaced.StatusCode);
            Assert.True((await ReadJsonAsync(replaced)).GetProperty("active").GetBoolean());
        }

        using (var scope = factory.Services.CreateScope()) {
            var users = scope.ServiceProvider.GetRequiredService<SchemataUserManager<SchemataUser>>();
            var user  = await FindAsync(scope, "Henry");
            Assert.False(await users.IsLockedOutAsync(user!));
            Assert.Null(user!.LockoutEnd);
            Assert.NotEqual(stamp, user.SecurityStamp);
        }

        using (var deactivated = await client.PutAsync($"{Users}/{id}", UserContent("Henry", "Henry", false))) {
            Assert.False((await ReadJsonAsync(deactivated)).GetProperty("active").GetBoolean());
        }

        using (var renamed = await client.PutAsync($"{Users}/{id}", UserContent("Henry-Renamed"))) {
            Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
            Assert.False((await ReadJsonAsync(renamed)).GetProperty("active").GetBoolean());
        }

        using var single = await client.GetAsync($"{Users}/{id}");
        Assert.False((await ReadJsonAsync(single)).GetProperty("active").GetBoolean());
    }

    [Fact]
    public async Task Leave_A_Temporary_Lockout_Active_Until_Deprovisioning() {
        using var factory = new WebAppFactory();
        using var client  = CreateClient(factory);

        var body = await CreateUserAsync(client, "Tina");
        var id   = body.GetProperty("id").GetString()!;

        using (var scope = factory.Services.CreateScope()) {
            var users = scope.ServiceProvider.GetRequiredService<SchemataUserManager<SchemataUser>>();
            var user  = await FindAsync(scope, "Tina");
            user!.LockoutEnabled = true;
            user.LockoutEnd      = DateTimeOffset.UtcNow.AddHours(1);
            Assert.True((await users.UpdateAsync(user)).Succeeded);
        }

        using (var response = await client.GetAsync($"{Users}/{id}")) {
            Assert.True((await ReadJsonAsync(response)).GetProperty("active").GetBoolean());
        }

        using (var deactivated = await client.PutAsync($"{Users}/{id}", UserContent("Tina", "Tina", false))) {
            Assert.False((await ReadJsonAsync(deactivated)).GetProperty("active").GetBoolean());
        }

        using (var scope = factory.Services.CreateScope()) {
            var user = await FindAsync(scope, "Tina");
            Assert.Equal(DateTimeOffset.MaxValue, user!.LockoutEnd);
        }

        using (var renamed = await client.PutAsync($"{Users}/{id}", UserContent("Tina-Provisioned"))) {
            Assert.False((await ReadJsonAsync(renamed)).GetProperty("active").GetBoolean());
        }

        using (var reactivated = await client.PutAsync($"{Users}/{id}", UserContent("Tina-Provisioned", "Tina", true))) {
            Assert.True((await ReadJsonAsync(reactivated)).GetProperty("active").GetBoolean());
        }

        using (var scope = factory.Services.CreateScope()) {
            var users = scope.ServiceProvider.GetRequiredService<SchemataUserManager<SchemataUser>>();
            var user  = await FindAsync(scope, "Tina-Provisioned");
            Assert.False(await users.IsLockedOutAsync(user!));
        }
    }

    [Fact]
    public async Task Reject_Unauthenticated_And_Unauthorized_Requests() {
        using (var factory = new WebAppFactory()) {
            using var anonymous = CreateClient(factory, authenticated: false);
            using var get       = await anonymous.GetAsync(Users);
            Assert.Equal(HttpStatusCode.Unauthorized, get.StatusCode);
            AssertScimError(await ReadJsonAsync(get), "401");
            using var post = await anonymous.PostAsync(Users, UserContent("Ivan"));
            Assert.Equal(HttpStatusCode.Unauthorized, post.StatusCode);
            AssertScimError(await ReadJsonAsync(post), "401");
        }

        using (var factory = new WebAppFactory("Denied")) {
            using var client = CreateClient(factory);
            using var denied = await client.GetAsync(Users);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            AssertScimError(await ReadJsonAsync(denied), "403");
        }

        using (var factory = new WebAppFactory("ScimPolicy")) {
            using var client = CreateClient(factory);
            using var denied = await client.GetAsync(Users);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            AssertScimError(await ReadJsonAsync(denied), "403");
        }
    }

    [Fact]
    public async Task Register_The_User_Resource_Type_And_Schema_With_Discovery() {
        using var factory = new WebAppFactory();
        using var client  = CreateClient(factory);

        using (var response = await client.GetAsync("/scim/v2/ResourceTypes")) {
            Assert.Equal(ScimConstants.MediaType, response.Content.Headers.ContentType?.MediaType);
            var resources = (await ReadJsonAsync(response)).GetProperty("Resources");
            var user = resources.EnumerateArray().Single(r => r.GetProperty("name").GetString() == "User");
            Assert.Equal("/Users", user.GetProperty("endpoint").GetString());
            Assert.Equal(ScimConstants.UserSchema, user.GetProperty("schema").GetString());
        }

        using (var response = await client.GetAsync("/scim/v2/ResourceTypes/User")) {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("User", (await ReadJsonAsync(response)).GetProperty("name").GetString());
        }

        using (var response = await client.GetAsync($"/scim/v2/Schemas/{Uri.EscapeDataString(ScimConstants.UserSchema)}")) {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var attributes = (await ReadJsonAsync(response)).GetProperty("attributes");
            var userName = attributes.EnumerateArray().Single(a => a.GetProperty("name").GetString() == "userName");
            Assert.True(userName.GetProperty("required").GetBoolean());
            Assert.Equal(ScimConstants.UniquenessServer, userName.GetProperty("uniqueness").GetString());
            Assert.Equal(ScimConstants.ReadWrite, userName.GetProperty("mutability").GetString());
            var password = attributes.EnumerateArray().Single(a => a.GetProperty("name").GetString() == "password");
            Assert.Equal(ScimConstants.WriteOnly, password.GetProperty("mutability").GetString());
            Assert.Equal(ScimConstants.Never, password.GetProperty("returned").GetString());
            var groups = attributes.EnumerateArray().Single(a => a.GetProperty("name").GetString() == "groups");
            Assert.Equal(ScimConstants.ReadOnly, groups.GetProperty("mutability").GetString());
            Assert.True(groups.GetProperty("multiValued").GetBoolean());
            var subAttributes = groups.GetProperty("subAttributes").EnumerateArray().ToList();
            Assert.Equal(4, subAttributes.Count);
            Assert.All(subAttributes, attribute => Assert.Equal(ScimConstants.ReadOnly, attribute.GetProperty("mutability").GetString()));
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
        string?    displayName = null,
        bool?      active      = null
    ) {
        using var response = await client.PostAsync(Users, UserContent(userName, displayName, active));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return await ReadJsonAsync(response);
    }

    private static StringContent UserContent(
        string                     userName,
        string?                    displayName = null,
        bool?                      active      = null,
        string?                    password    = null,
        string?                    id          = null,
        object?                    groups      = null,
        object?                    meta        = null,
        IReadOnlyList<string>?     schemas     = null
    ) {
        var payload = new Dictionary<string, object?> { ["userName"] = userName };
        if (displayName is not null) payload["displayName"] = displayName;
        if (active is not null) payload["active"]           = active;
        if (password is not null) payload["password"]       = password;
        if (id is not null) payload["id"]                   = id;
        if (groups is not null) payload["groups"]           = groups;
        if (meta is not null) payload["meta"]               = meta;
        if (schemas is not null) payload["schemas"]         = schemas;

        return new(JsonSerializer.Serialize(payload), Encoding.UTF8, ScimConstants.MediaType);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return document.RootElement.Clone();
    }

    private static void AssertScimError(JsonElement root, string status, string? scimType = null) {
        Assert.Equal(ScimConstants.ErrorSchema, root.GetProperty("schemas")[0].GetString());
        Assert.Equal(status, root.GetProperty("status").GetString());
        if (scimType is null) {
            Assert.False(root.TryGetProperty("scimType", out _));
        } else {
            Assert.Equal(scimType, root.GetProperty("scimType").GetString());
        }

        Assert.False(root.TryGetProperty("error", out _));
    }

    private static Task<SchemataUser?> FindAsync(IServiceScope scope, string userName) {
        return scope.ServiceProvider.GetRequiredService<SchemataUserManager<SchemataUser>>().FindByNameAsync(userName);
    }
}
