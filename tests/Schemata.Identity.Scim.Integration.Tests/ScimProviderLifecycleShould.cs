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
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.Repository;
using Schemata.Identity.Foundation;
using Schemata.Identity.Scim.Integration.Tests.Fixtures;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Managers;
using Schemata.Scim;
using Schemata.Scim.Entities;
using Xunit;

namespace Schemata.Identity.Scim.Integration.Tests;

[Trait("Layer", "Integration")]
public sealed class ScimProviderLifecycleShould
{
    private const string Users = "/scim/v2/Users";
    private const string Groups = "/scim/v2/Groups";

    [Theory]
    [InlineData("EfCore")]
    [InlineData("LinqToDb")]
    public async Task Derived_Users_And_Groups_Share_Native_Manager_State(string provider) {
        using var factory = new WebAppFactory($"Provider{provider}");
        using var client = Client(factory);
        using var other = Client(factory, "domain-b");

        using (var discovery = await client.GetAsync("/scim/v2/ServiceProviderConfig")) {
            var root = await JsonAsync(discovery);
            Assert.True(root.GetProperty("patch").GetProperty("supported").GetBoolean());
            Assert.True(root.GetProperty("filter").GetProperty("supported").GetBoolean());
            Assert.False(root.GetProperty("bulk").GetProperty("supported").GetBoolean());
            Assert.False(root.GetProperty("sort").GetProperty("supported").GetBoolean());
            Assert.False(root.GetProperty("etag").GetProperty("supported").GetBoolean());
        }
        using (var schema = await client.GetAsync($"/scim/v2/Schemas/{Uri.EscapeDataString(ScimConstants.UserSchema)}")) {
            var attributes = (await JsonAsync(schema)).GetProperty("attributes").EnumerateArray()
                .ToDictionary(attribute => attribute.GetProperty("name").GetString()!);
            Assert.Equal("string", attributes["userName"].GetProperty("type").GetString());
            Assert.False(attributes["userName"].GetProperty("caseExact").GetBoolean());
            Assert.Equal("boolean", attributes["active"].GetProperty("type").GetString());
            Assert.Equal(ScimConstants.ReadOnly, attributes["groups"].GetProperty("mutability").GetString());
            Assert.False(attributes.ContainsKey("password"));
        }

        var created = await CreateUserAsync(client, "Ｐｒｏｖｉｄｅｒ", "Original", "shared-external", false);
        var id = created.GetProperty("id").GetString()!;
        Assert.Equal("shared-external", created.GetProperty("externalId").GetString());
        AssertRedacted(created);
        var second = await CreateUserAsync(client, "Second.Provider", "Second", "second-external");
        var secondId = second.GetProperty("id").GetString()!;
        var foreign = await CreateUserAsync(other, "Foreign.Provider", externalId: "shared-external");
        Assert.NotEqual(id, foreign.GetProperty("id").GetString());

        using (var scope = factory.Services.CreateScope()) {
            var manager = scope.ServiceProvider.GetRequiredService<SchemataUserManager<SchemataUser>>();
            var stored = await manager.FindByIdAsync(id);
            Assert.NotNull(stored);
            Assert.Equal("provider", stored.NormalizedUserName);
            Assert.False(string.IsNullOrWhiteSpace(stored.Name));
            Assert.False(stored.AdministrativelyActive);
            Assert.True((await manager.AddPasswordAsync(stored, "Provider-admission-1")).Succeeded);
            var signIn = scope.ServiceProvider.GetRequiredService<SchemataSignInManager<SchemataUser>>();
            var denied = await signIn.CheckPasswordSignInAsync(stored, "Provider-admission-1", false);
            Assert.True(denied.IsNotAllowed);
            stored.DisplayName = "Native manager";
            Assert.True((await manager.UpdateAsync(stored)).Succeeded);
        }
        using (var read = await client.GetAsync($"{Users}/{id}")) {
            Assert.Equal("Native manager", (await JsonAsync(read)).GetProperty("displayName").GetString());
        }

        using (var page = await client.GetAsync($"{Users}?startIndex=2&count=1")) {
            var root = await JsonAsync(page);
            Assert.Equal(3, root.GetProperty("totalResults").GetInt32());
            Assert.Equal(2, root.GetProperty("startIndex").GetInt32());
            Assert.Equal(1, root.GetProperty("itemsPerPage").GetInt32());
            Assert.Single(root.GetProperty("Resources").EnumerateArray());
        }
        using (var filtered = await client.GetAsync($"{Users}?filter={Uri.EscapeDataString("USERNAME eq \"PROVIDER\"")}&attributes=userName")) {
            var root = await JsonAsync(filtered);
            Assert.Equal(1, root.GetProperty("totalResults").GetInt32());
            var resource = Assert.Single(root.GetProperty("Resources").EnumerateArray());
            Assert.Equal("Ｐｒｏｖｉｄｅｒ", resource.GetProperty("userName").GetString());
            Assert.False(resource.TryGetProperty("displayName", out _));
        }

        using (var patched = await client.PatchAsync($"{Users}/{id}", Patch(
            Operation("replace", "displayName", "SCIM patched"),
            Operation("replace", "active", true)))) {
            var root = await JsonAsync(patched);
            Assert.Equal(id, root.GetProperty("id").GetString());
            Assert.True(root.GetProperty("active").GetBoolean());
            AssertRedacted(root);
        }
        using (var scope = factory.Services.CreateScope()) {
            var manager = scope.ServiceProvider.GetRequiredService<SchemataUserManager<SchemataUser>>();
            var stored = await manager.FindByIdAsync(id);
            Assert.NotNull(stored);
            Assert.Equal("SCIM patched", stored.DisplayName);
            Assert.True(stored.AdministrativelyActive);
            var signIn = scope.ServiceProvider.GetRequiredService<SchemataSignInManager<SchemataUser>>();
            Assert.True((await signIn.CheckPasswordSignInAsync(stored, "Provider-admission-1", false)).Succeeded);
        }

        var groupFirst = await CreateUserAsync(client, "Group.Member.First");
        var groupSecond = await CreateUserAsync(client, "Group.Member.Second");
        var groupFirstId = groupFirst.GetProperty("id").GetString()!;
        var groupSecondId = groupSecond.GetProperty("id").GetString()!;
        var group = await CreateGroupAsync(client, "Provider group", "group-external", Member(groupFirstId));
        var groupId = group.GetProperty("id").GetString()!;
        using (var scope = factory.Services.CreateScope()) {
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<ProviderRole>>();
            var role = await roles.FindByIdAsync(groupId);
            Assert.NotNull(role);
            Assert.IsType<ProviderRole>(role);
            role.DisplayName = "Native role";
            Assert.True((await roles.UpdateAsync(role)).Succeeded);
        }
        using (var read = await client.GetAsync($"{Groups}/{groupId}")) {
            Assert.Equal("Native role", (await JsonAsync(read)).GetProperty("displayName").GetString());
        }

        using (var patched = await client.PatchAsync($"{Groups}/{groupId}", Patch(
            new Dictionary<string, object?> { ["op"] = "remove", ["path"] = $"members[value eq \"{groupFirstId}\"]" },
            Operation("add", "members", new[] { Member(groupSecondId) }),
            Operation("replace", "displayName", "SCIM group")))) {
            var root = await JsonAsync(patched);
            Assert.Equal(groupId, root.GetProperty("id").GetString());
            Assert.Equal(groupSecondId, Assert.Single(root.GetProperty("members").EnumerateArray()).GetProperty("value").GetString());
        }
        using (var former = await client.GetAsync($"{Users}/{groupFirstId}")) {
            Assert.Empty((await JsonAsync(former)).GetProperty("groups").EnumerateArray());
        }
        using (var current = await client.GetAsync($"{Users}/{groupSecondId}")) {
            Assert.Equal(groupId, Assert.Single((await JsonAsync(current)).GetProperty("groups").EnumerateArray())
                .GetProperty("value").GetString());
        }
        using (var scope = factory.Services.CreateScope()) {
            var role = await scope.ServiceProvider.GetRequiredService<RoleManager<ProviderRole>>().FindByIdAsync(groupId);
            Assert.NotNull(role);
            Assert.Equal("SCIM group", role.DisplayName);
            Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<IRepository<SchemataUserRole>>().CountAsync(query => query));
        }

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"{Groups}/{groupId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"{Users}/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"{Users}/{secondId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"{Users}/{groupFirstId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"{Users}/{groupSecondId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await other.DeleteAsync($"{Users}/{foreign.GetProperty("id").GetString()}")).StatusCode);
        using var verification = factory.Services.CreateScope();
        Assert.Equal(0, await verification.ServiceProvider.GetRequiredService<IRepository<SchemataUserRole>>().CountAsync(query => query));
        Assert.Equal(0, await verification.ServiceProvider.GetRequiredService<IRepository<ScimProvisioningBinding>>().CountAsync(query => query));
        Assert.Null(await verification.ServiceProvider.GetRequiredService<RoleManager<ProviderRole>>().FindByIdAsync(groupId));
        Assert.Null(await verification.ServiceProvider.GetRequiredService<SchemataUserManager<SchemataUser>>().FindByIdAsync(id));
    }

    [Theory]
    [InlineData("EfCore")]
    [InlineData("LinqToDb")]
    public async Task Failed_Writes_And_Stale_Native_Updates_Preserve_Both_Views(string provider) {
        using var factory = new WebAppFactory($"Provider{provider}BindingFailure");
        using var client = Client(factory);
        var created = await CreateUserAsync(client, "Concurrency.Provider", "Original", "before-external");
        var id = created.GetProperty("id").GetString()!;

        using (var firstScope = factory.Services.CreateScope())
        using (var secondScope = factory.Services.CreateScope()) {
            var firstManager = firstScope.ServiceProvider.GetRequiredService<SchemataUserManager<SchemataUser>>();
            var secondManager = secondScope.ServiceProvider.GetRequiredService<SchemataUserManager<SchemataUser>>();
            var first = await firstManager.FindByIdAsync(id);
            var second = await secondManager.FindByIdAsync(id);
            Assert.NotNull(first);
            Assert.NotNull(second);
            first.DisplayName = "Concurrent winner";
            second.DisplayName = "Stale loser";
            Assert.True((await firstManager.UpdateAsync(first)).Succeeded);
            IdentityResult? stale = null;
            var conflict = await Record.ExceptionAsync(async () => stale = await secondManager.UpdateAsync(second));
            if (conflict is not null) {
                Assert.IsType<AbortedException>(conflict);
            } else {
                Assert.NotNull(stale);
                Assert.False(stale.Succeeded);
                Assert.Contains(stale.Errors, error => error.Code == nameof(IdentityErrorDescriber.ConcurrencyFailure));
            }
        }

        using (var response = await client.PatchAsync($"{Users}/{id}", Patch(
            Operation("replace", "displayName", "Rolled back"),
            Operation("replace", "externalId", "fail-binding")))) {
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        }
        using (var read = await client.GetAsync($"{Users}/{id}")) {
            var root = await JsonAsync(read);
            Assert.Equal("Concurrent winner", root.GetProperty("displayName").GetString());
            Assert.Equal("before-external", root.GetProperty("externalId").GetString());
        }
        using var verification = factory.Services.CreateScope();
        var stored = await verification.ServiceProvider.GetRequiredService<SchemataUserManager<SchemataUser>>().FindByIdAsync(id);
        Assert.NotNull(stored);
        Assert.Equal("Concurrent winner", stored.DisplayName);
    }

    [Theory]
    [InlineData("EfCore")]
    [InlineData("LinqToDb")]
    public async Task Failed_Group_Membership_Rolls_Back_Role_Binding_And_Links(string provider) {
        using var factory = new WebAppFactory($"Provider{provider}MembershipFailure");
        using var client = Client(factory);
        var first = await CreateUserAsync(client, "Group.Rollback.First");
        var second = await CreateUserAsync(client, "Group.Rollback.Second");
        var third = await CreateUserAsync(client, "Group.Rollback.Third");
        var firstId = first.GetProperty("id").GetString()!;
        var group = await CreateGroupAsync(client, "Original group", "before-group", Member(firstId));
        var groupId = group.GetProperty("id").GetString()!;

        using var response = await client.PatchAsync($"{Groups}/{groupId}", Patch(
            Operation("replace", "displayName", "Changed group"),
            Operation("replace", "externalId", "after-group"),
            Operation("add", "members", new[] {
                Member(second.GetProperty("id").GetString()!),
                Member(third.GetProperty("id").GetString()!),
            })));
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        using (var read = await client.GetAsync($"{Groups}/{groupId}")) {
            var root = await JsonAsync(read);
            Assert.Equal("Original group", root.GetProperty("displayName").GetString());
            Assert.Equal("before-group", root.GetProperty("externalId").GetString());
            Assert.Equal(firstId, Assert.Single(root.GetProperty("members").EnumerateArray()).GetProperty("value").GetString());
        }
        using var verification = factory.Services.CreateScope();
        var role = await verification.ServiceProvider.GetRequiredService<RoleManager<ProviderRole>>().FindByIdAsync(groupId);
        Assert.NotNull(role);
        Assert.Equal("Original group", role.DisplayName);
        Assert.Equal(1, await verification.ServiceProvider.GetRequiredService<IRepository<SchemataUserRole>>().CountAsync(query => query));
    }

    [Theory]
    [InlineData("EfCore")]
    [InlineData("LinqToDb")]
    public async Task Unsupported_Profile_Boundaries_Are_Provider_Independent(string provider) {
        using var factory = new WebAppFactory($"Provider{provider}");
        using var client = Client(factory);
        const string secret = "newman-secret-not-for-output";
        using (var password = await client.PostAsync(Users, Json(new {
            schemas = new[] { ScimConstants.UserSchema }, userName = "Password.Provider", password = secret,
        }))) {
            var content = await password.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.BadRequest, password.StatusCode);
            Assert.DoesNotContain(secret, content, StringComparison.Ordinal);
        }
        using (var nested = await client.PostAsync(Groups, Json(new {
            schemas = new[] { ScimConstants.GroupSchema }, displayName = "Nested",
            members = new[] { Member(Guid.NewGuid().ToString(), "Group") },
        }))) {
            Assert.Equal(HttpStatusCode.BadRequest, nested.StatusCode);
        }
        using (var bulk = await client.PostAsync("/scim/v2/Bulk", Json(new { }))) {
            Assert.Equal(HttpStatusCode.NotImplemented, bulk.StatusCode);
        }
        using (var sorted = await client.GetAsync($"{Users}?sortBy=userName")) {
            Assert.Equal(HttpStatusCode.NotImplemented, sorted.StatusCode);
        }
        using (var enterprise = await client.GetAsync($"/scim/v2/Schemas/{Uri.EscapeDataString(ScimConstants.EnterpriseUserSchema)}")) {
            Assert.Equal(HttpStatusCode.NotFound, enterprise.StatusCode);
        }
        using (var events = await client.GetAsync("/scim/v2/Events")) {
            Assert.Equal(HttpStatusCode.NotFound, events.StatusCode);
        }
        using (var cursor = await client.GetAsync($"{Users}?cursor=opaque")) {
            Assert.Equal(HttpStatusCode.OK, cursor.StatusCode);
            Assert.Equal(1, (await JsonAsync(cursor)).GetProperty("startIndex").GetInt32());
            Assert.False(cursor.Headers.Contains("ETag"));
        }
    }

    private static HttpClient Client(WebAppFactory factory, string domain = "domain-a") {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Scim", "1");
        client.DefaultRequestHeaders.Add("X-Test-Domain", domain);
        return client;
    }

    private static async Task<JsonElement> CreateUserAsync(
        HttpClient client,
        string userName,
        string? displayName = null,
        string? externalId = null,
        bool? active = null
    ) {
        using var response = await client.PostAsync(Users, Json(new {
            schemas = new[] { ScimConstants.UserSchema }, userName, displayName, externalId = externalId ?? $"external-{userName}", active,
        }));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await JsonAsync(response);
    }

    private static async Task<JsonElement> CreateGroupAsync(HttpClient client, string displayName, string externalId, params object[] members) {
        using var response = await client.PostAsync(Groups, Json(new {
            schemas = new[] { ScimConstants.GroupSchema }, displayName, externalId, members,
        }));
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await JsonAsync(response);
    }

    private static Dictionary<string, object?> Member(string value, string type = "User") {
        return new() { ["value"] = value, ["type"] = type };
    }

    private static Dictionary<string, object?> Operation(string op, string path, object value) {
        return new() { ["op"] = op, ["path"] = path, ["value"] = value };
    }

    private static StringContent Patch(params object[] operations) {
        return Json(new { schemas = new[] { ScimConstants.PatchOpSchema }, Operations = operations });
    }

    private static StringContent Json(object body) {
        return new(JsonSerializer.Serialize(body), Encoding.UTF8, ScimConstants.MediaType);
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static void AssertRedacted(JsonElement resource) {
        foreach (var field in new[] { "password", "passwordHash", "securityStamp", "concurrencyStamp", "accessToken" }) {
            Assert.False(resource.TryGetProperty(field, out _));
        }
    }
}
