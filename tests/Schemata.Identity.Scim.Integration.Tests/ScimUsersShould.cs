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
using Schemata.Entity.Repository;
using Schemata.Identity.Scim.Integration.Tests.Fixtures;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Managers;
using Schemata.Scim;
using Schemata.Scim.Entities;
using Xunit;

namespace Schemata.Identity.Scim.Integration.Tests;

[Trait("Layer", "Integration")]
public sealed class ScimUsersShould
{
    private const string Users = "/scim/v2/Users";

    [Fact]
    public async Task Complete_Lifecycle_Preserves_Uid_And_Commits_Bindings_And_Dependents() {
        using var factory = new WebAppFactory();
        using var client = CreateClient(factory);
        var createdBody = await CreateUserAsync(client, "Ａｌｉｃｅ", "Alice Example", "external-1");
        var id = createdBody.GetProperty("id").GetString()!;
        Assert.Equal("Ａｌｉｃｅ", createdBody.GetProperty("userName").GetString());
        Assert.Equal("external-1", createdBody.GetProperty("externalId").GetString());
        Assert.False(createdBody.TryGetProperty("passwordHash", out _));
        Assert.False(createdBody.TryGetProperty("securityStamp", out _));
        Assert.False(createdBody.TryGetProperty("concurrencyStamp", out _));

        using (var scope = factory.Services.CreateScope()) {
            var manager = scope.ServiceProvider.GetRequiredService<SchemataUserManager<SchemataUser>>();
            var stored = await manager.FindByNameAsync("alice");
            Assert.NotNull(stored);
            Assert.Equal(id, stored.Uid.ToString());
            Assert.Equal("Ａｌｉｃｅ", stored.UserName);
            Assert.Equal("alice", stored.NormalizedUserName);
        }

        using (var filtered = await client.GetAsync($"{Users}?filter={Uri.EscapeDataString("userName eq \"ALICE\"")}")) {
            var result = await ReadJsonAsync(filtered);
            Assert.Equal(1, result.GetProperty("totalResults").GetInt32());
            Assert.Equal(id, result.GetProperty("Resources")[0].GetProperty("id").GetString());
        }

        using (var replaced = await client.PutAsync($"{Users}/{id}", UserContent("Alice-Renamed", "Renamed", true, "external-1"))) {
            Assert.Equal(HttpStatusCode.OK, replaced.StatusCode);
            var result = await ReadJsonAsync(replaced);
            Assert.Equal(id, result.GetProperty("id").GetString());
            Assert.Equal("Alice-Renamed", result.GetProperty("userName").GetString());
            Assert.Equal("Renamed", result.GetProperty("displayName").GetString());
        }

        using (var patched = await client.PatchAsync($"{Users}/{id}", PatchContent(
            Operation("replace", "displayName", "Patched"),
            Operation("replace", "active", false)))) {
            Assert.Equal(HttpStatusCode.OK, patched.StatusCode);
            var result = await ReadJsonAsync(patched);
            Assert.Equal(id, result.GetProperty("id").GetString());
            Assert.Equal("Patched", result.GetProperty("displayName").GetString());
            Assert.False(result.GetProperty("active").GetBoolean());
        }

        using (var scope = factory.Services.CreateScope()) {
            var manager = scope.ServiceProvider.GetRequiredService<SchemataUserManager<SchemataUser>>();
            var stored = await manager.FindByIdAsync(id);
            Assert.NotNull(stored);
            Assert.False(stored.AdministrativelyActive);
            Assert.True((await manager.SetAuthenticationTokenAsync(stored, "test", "dependent", "secret")).Succeeded);
        }

        using (var deleted = await client.DeleteAsync($"{Users}/{id}")) {
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        }

        using (var missing = await client.GetAsync($"{Users}/{id}")) {
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }

        using var verification = factory.Services.CreateScope();
        var services = verification.ServiceProvider;
        Assert.Equal(0, await services.GetRequiredService<IRepository<ScimProvisioningBinding>>().CountAsync(q => q));
        Assert.Equal(0, await services.GetRequiredService<IRepository<SchemataUserToken>>().CountAsync(q => q));
    }

    [Theory]
    [InlineData("co", " ")]
    [InlineData("sw", "ＡＬＩＣＥ ")]
    [InlineData("ew", " SMITH")]
    public async Task Username_Substrings_Allow_Partial_Userparts(string op, string value) {
        using var factory = new WebAppFactory();
        using var client = CreateClient(factory);
        var created = await CreateUserAsync(client, "Alice Smith");
        var id = created.GetProperty("id").GetString();
        var filter = $"userName {op} {JsonSerializer.Serialize(value)}";

        using var get = await client.GetAsync($"{Users}?filter={Uri.EscapeDataString(filter)}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var getResult = await ReadJsonAsync(get);
        Assert.Equal(id, Assert.Single(getResult.GetProperty("Resources").EnumerateArray()).GetProperty("id").GetString());
        using var post = await client.PostAsync($"{Users}/.search", new StringContent(JsonSerializer.Serialize(new {
            schemas = new[] { "urn:ietf:params:scim:api:messages:2.0:SearchRequest" }, filter,
        }), Encoding.UTF8, "application/scim+json"));
        Assert.Equal(HttpStatusCode.OK, post.StatusCode);
        var postResult = await ReadJsonAsync(post);
        Assert.Equal(id, Assert.Single(postResult.GetProperty("Resources").EnumerateArray()).GetProperty("id").GetString());
    }

    [Fact]
    public async Task External_Ids_Are_Independent_In_Each_Provisioning_Domain() {
        using var factory = new WebAppFactory();
        using var first = CreateClient(factory, domain: "domain-a");
        using var second = CreateClient(factory, domain: "domain-b");
        var alpha = await CreateUserAsync(first, "Domain-Alpha", externalId: "shared-external");
        var beta = await CreateUserAsync(second, "Domain-Beta", externalId: "shared-external");
        var alphaId = alpha.GetProperty("id").GetString()!;
        var betaId = beta.GetProperty("id").GetString()!;
        Assert.NotEqual(alphaId, betaId);

        using (var duplicate = await first.PostAsync(Users, UserContent("Domain-Duplicate", externalId: "shared-external"))) {
            Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
            AssertScimError(await ReadJsonAsync(duplicate), "409", ScimConstants.Uniqueness);
        }

        using (var view = await second.GetAsync($"{Users}/{alphaId}")) {
            Assert.Equal(HttpStatusCode.OK, view.StatusCode);
            Assert.False((await ReadJsonAsync(view)).TryGetProperty("externalId", out _));
        }

        using (var view = await second.GetAsync($"{Users}/{betaId}")) {
            Assert.Equal("shared-external", (await ReadJsonAsync(view)).GetProperty("externalId").GetString());
        }

        using var scope = factory.Services.CreateScope();
        var bindings = await scope.ServiceProvider.GetRequiredService<IRepository<ScimProvisioningBinding>>()
                                  .ListAsync(q => q.OrderBy(binding => binding.ProvisioningDomain)).ToListAsync();
        Assert.Equal(2, bindings.Count);
        Assert.Equal(new[] { "domain-a", "domain-b" }, bindings.Select(binding => binding.ProvisioningDomain));
        Assert.All(bindings, binding => Assert.Equal("shared-external", binding.ExternalId));
    }

    [Fact]
    public async Task Matching_Names_Do_Not_Adopt_An_Existing_User() {
        using var factory = new WebAppFactory();
        Guid existingUid;
        using (var scope = factory.Services.CreateScope()) {
            var manager = scope.ServiceProvider.GetRequiredService<SchemataUserManager<SchemataUser>>();
            var existing = new SchemataUser { UserName = "Existing", DisplayName = "Shared Display" };
            Assert.True((await manager.CreateAsync(existing)).Succeeded);
            existingUid = existing.Uid;
        }

        using var client = CreateClient(factory);
        using var response = await client.PostAsync(Users, UserContent("existing", "Shared Display", externalId: "new-external"));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        AssertScimError(await ReadJsonAsync(response), "409", ScimConstants.Uniqueness);

        using var verification = factory.Services.CreateScope();
        var users = verification.ServiceProvider.GetRequiredService<IRepository<SchemataUser>>();
        var stored = await users.SingleOrDefaultAsync(q => q.Where(user => user.Uid == existingUid));
        Assert.NotNull(stored);
        Assert.Equal("Existing", stored.UserName);
        Assert.Equal(0, await verification.ServiceProvider.GetRequiredService<IRepository<ScimProvisioningBinding>>().CountAsync(q => q));
    }

    [Fact]
    public async Task Provisioning_Issues_No_Credentials_And_Ignores_ReadOnly_Groups() {
        using var factory = new WebAppFactory();
        using var client = CreateClient(factory);
        using var created = await client.PostAsync(Users, UserContent(
            "No-Credentials", "Provisioned", true, id: "client-id",
            groups: new[] { new Dictionary<string, string> { ["value"] = "forged-group" } },
            meta: new Dictionary<string, object> { ["created"] = "2000-01-01T00:00:00Z" }));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.False(created.Headers.Contains("Set-Cookie"));
        var body = await ReadJsonAsync(created);
        var id = body.GetProperty("id").GetString()!;
        Assert.NotEqual("client-id", id);
        Assert.Equal(0, body.GetProperty("groups").GetArrayLength());
        Assert.NotEqual("2000-01-01T00:00:00Z", body.GetProperty("meta").GetProperty("created").GetString());
        foreach (var field in new[] { "password", "passwordHash", "securityStamp", "concurrencyStamp", "accessFailedCount", "emailConfirmed" }) {
            Assert.False(body.TryGetProperty(field, out _));
        }

        using var scope = factory.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<SchemataUserManager<SchemataUser>>();
        var stored = await manager.FindByIdAsync(id);
        Assert.NotNull(stored);
        Assert.Null(stored.PasswordHash);
        Assert.False(stored.EmailConfirmed);
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<IRepository<SchemataUserToken>>().CountAsync(q => q));
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<IRepository<SchemataUserLogin>>().CountAsync(q => q));
    }

    [Theory]
    [InlineData("password")]
    [InlineData("nested")]
    public async Task Password_Writes_Are_Redacted_And_Rejected(string location) {
        using var factory = new WebAppFactory();
        using var client = CreateClient(factory);
        const string secret = "do-not-echo-this-password";
        var payload = location == "password"
            ? new Dictionary<string, object?> { ["userName"] = "Secret-User", ["password"] = secret }
            : new Dictionary<string, object?> { ["userName"] = "Secret-User", ["meta"] = new Dictionary<string, object> { ["password"] = secret } };

        using var response = await client.PostAsync(Users, JsonContent(payload));
        var content = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(secret, content, StringComparison.Ordinal);
        AssertScimError(JsonDocument.Parse(content).RootElement, "400", ScimConstants.InvalidValue);

        using var list = await client.GetAsync(Users);
        Assert.Equal(0, (await ReadJsonAsync(list)).GetProperty("totalResults").GetInt32());
    }

    [Fact]
    public async Task Administrative_Active_Rotates_Stamp_Without_Clearing_Lockout() {
        using var factory = new WebAppFactory();
        using var client = CreateClient(factory);
        var created = await CreateUserAsync(client, "Administrative", active: false);
        var id = created.GetProperty("id").GetString()!;
        string? initialStamp;
        var lockout = DateTimeOffset.UtcNow.AddHours(2);
        using (var scope = factory.Services.CreateScope()) {
            var manager = scope.ServiceProvider.GetRequiredService<SchemataUserManager<SchemataUser>>();
            var stored = await manager.FindByIdAsync(id);
            Assert.NotNull(stored);
            Assert.False(stored.AdministrativelyActive);
            Assert.Null(stored.LockoutEnd);
            initialStamp = stored.SecurityStamp;
            stored.LockoutEnd = lockout;
            Assert.True((await manager.UpdateAsync(stored)).Succeeded);
        }

        using (var activated = await client.PutAsync($"{Users}/{id}", UserContent("Administrative", active: true))) {
            Assert.True((await ReadJsonAsync(activated)).GetProperty("active").GetBoolean());
        }

        using (var scope = factory.Services.CreateScope()) {
            var stored = await scope.ServiceProvider.GetRequiredService<SchemataUserManager<SchemataUser>>().FindByIdAsync(id);
            Assert.NotNull(stored);
            Assert.True(stored.AdministrativelyActive);
            Assert.Equal(lockout, stored.LockoutEnd);
            Assert.NotEqual(initialStamp, stored.SecurityStamp);
        }

        using var list = await client.GetAsync(Users);
        var resource = (await ReadJsonAsync(list)).GetProperty("Resources")[0];
        Assert.True(resource.GetProperty("active").GetBoolean());
    }

    [Theory]
    [InlineData("Ａｌｉｃｅ", "alice")]
    [InlineData("A\u030A", "å")]
    public async Task Duplicate_Normalized_Usernames_Conflict_Without_Changing_The_Original(string original, string duplicate) {
        using var factory = new WebAppFactory();
        using var client = CreateClient(factory);
        var created = await CreateUserAsync(client, original);
        var id = created.GetProperty("id").GetString()!;

        using var response = await client.PostAsync(Users, UserContent(duplicate));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        AssertScimError(await ReadJsonAsync(response), "409", ScimConstants.Uniqueness);

        using var verification = factory.Services.CreateScope();
        var stored = await verification.ServiceProvider.GetRequiredService<SchemataUserManager<SchemataUser>>().FindByIdAsync(id);
        Assert.NotNull(stored);
        Assert.Equal(original, stored.UserName);
        Assert.Equal(1, await verification.ServiceProvider.GetRequiredService<IRepository<SchemataUser>>().CountAsync(q => q));
    }

    [Fact]
    public async Task Missing_Store_Capability_Fails_Without_A_Partial_User() {
        using var factory = new WebAppFactory("MissingCapability");
        using var client = CreateClient(factory);

        using var response = await client.PostAsync(Users, UserContent("Unsupported"));
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        AssertScimError(await ReadJsonAsync(response), "500");

        using var scope = factory.Services.CreateScope();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<IRepository<SchemataUser>>().CountAsync(q => q));
    }

    [Fact]
    public async Task Binding_Failure_Rolls_Back_Every_Patch_Change() {
        using var factory = new WebAppFactory("BindingFailure");
        using var client = CreateClient(factory);
        var created = await CreateUserAsync(client, "Rollback", "Original");
        var id = created.GetProperty("id").GetString()!;

        using var response = await client.PatchAsync($"{Users}/{id}", PatchContent(
            Operation("replace", "displayName", "Changed"),
            Operation("replace", "externalId", "fail-binding")));
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<SchemataUserManager<SchemataUser>>().FindByIdAsync(id);
        Assert.NotNull(stored);
        Assert.Equal("Original", stored.DisplayName);
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<IRepository<ScimProvisioningBinding>>().CountAsync(q => q));
    }

    [Fact]
    public async Task Put_Does_Not_Create_A_Missing_User() {
        using var factory = new WebAppFactory();
        using var client = CreateClient(factory);
        var id = Guid.NewGuid();

        using var response = await client.PutAsync($"{Users}/{id}", UserContent("Missing"));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<IRepository<SchemataUser>>().CountAsync(q => q));
    }

    [Fact]
    public async Task Cross_Domain_Writes_Cannot_Adopt_Or_Delete_A_Bound_User() {
        using var factory = new WebAppFactory();
        using var owner = CreateClient(factory, domain: "domain-a");
        using var other = CreateClient(factory, domain: "domain-b");
        var created = await CreateUserAsync(owner, "Owned-User", "Original", "owned-external");
        var id = created.GetProperty("id").GetString()!;

        using (var replaced = await other.PutAsync($"{Users}/{id}", UserContent("Adopted", externalId: "other-external"))) {
            Assert.Equal(HttpStatusCode.Forbidden, replaced.StatusCode);
        }
        using (var patched = await other.PatchAsync($"{Users}/{id}", PatchContent(Operation("replace", "displayName", "Changed")))) {
            Assert.Equal(HttpStatusCode.Forbidden, patched.StatusCode);
        }
        using (var deleted = await other.DeleteAsync($"{Users}/{id}")) {
            Assert.Equal(HttpStatusCode.Forbidden, deleted.StatusCode);
        }

        using (var scope = factory.Services.CreateScope()) {
            var stored = await scope.ServiceProvider.GetRequiredService<SchemataUserManager<SchemataUser>>().FindByIdAsync(id);
            Assert.NotNull(stored);
            Assert.Equal("Owned-User", stored.UserName);
            Assert.Equal("Original", stored.DisplayName);
            Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<IRepository<ScimProvisioningBinding>>().CountAsync(q => q));
        }

        using var ownerDelete = await owner.DeleteAsync($"{Users}/{id}");
        Assert.Equal(HttpStatusCode.NoContent, ownerDelete.StatusCode);
        using var verification = factory.Services.CreateScope();
        Assert.Equal(0, await verification.ServiceProvider.GetRequiredService<IRepository<ScimProvisioningBinding>>().CountAsync(q => q));
    }

    private static HttpClient CreateClient(WebAppFactory factory, bool authenticated = true, string domain = "domain-a") {
        var client = factory.CreateClient();
        if (authenticated) {
            client.DefaultRequestHeaders.Add("X-Test-Scim", "1");
            client.DefaultRequestHeaders.Add("X-Test-Domain", domain);
        }

        return client;
    }

    private static async Task<JsonElement> CreateUserAsync(
        HttpClient client,
        string userName,
        string? displayName = null,
        string? externalId = null,
        bool? active = null) {
        using var response = await client.PostAsync(Users, UserContent(userName, displayName, active, externalId));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadJsonAsync(response);
    }

    private static StringContent UserContent(
        string userName,
        string? displayName = null,
        bool? active = null,
        string? externalId = null,
        string? id = null,
        object? groups = null,
        object? meta = null) {
        var payload = new Dictionary<string, object?> {
            ["schemas"] = new[] { ScimConstants.UserSchema },
            ["userName"] = userName,
        };
        if (displayName is not null) payload["displayName"] = displayName;
        if (active is not null) payload["active"] = active;
        if (externalId is not null) payload["externalId"] = externalId;
        if (id is not null) payload["id"] = id;
        if (groups is not null) payload["groups"] = groups;
        if (meta is not null) payload["meta"] = meta;
        return JsonContent(payload);
    }

    private static Dictionary<string, object?> Operation(string op, string path, object value) {
        return new() { ["op"] = op, ["path"] = path, ["value"] = value };
    }

    private static StringContent PatchContent(params object[] operations) {
        return JsonContent(new Dictionary<string, object?> {
            ["schemas"] = new[] { ScimConstants.PatchOpSchema },
            ["Operations"] = operations,
        });
    }

    private static StringContent JsonContent(object payload) {
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
}
