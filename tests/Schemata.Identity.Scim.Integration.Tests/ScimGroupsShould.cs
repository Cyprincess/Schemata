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
using Schemata.Scim;
using Schemata.Scim.Entities;
using Xunit;

namespace Schemata.Identity.Scim.Integration.Tests;

[Trait("Layer", "Integration")]
public sealed class ScimGroupsShould
{
    private const string Groups = "/scim/v2/Groups";
    private const string Users = "/scim/v2/Users";

    [Fact]
    public async Task Relationship_Uris_Use_Configured_BasePath_And_Request_PathBase() {
        using var factory = new WebAppFactory("CustomPath");
        using var client = CreateClient(factory);
        const string root = "/host/directory";
        using var userResponse = await client.PostAsync(root + "/Users", new StringContent(JsonSerializer.Serialize(new {
            schemas = new[] { ScimConstants.UserSchema }, userName = "rooted-user", externalId = "rooted-user",
        }), Encoding.UTF8, "application/scim+json"));
        Assert.Equal(HttpStatusCode.Created, userResponse.StatusCode);
        var user = await ReadJsonAsync(userResponse);
        var userId = user.GetProperty("id").GetString();
        using var groupResponse = await client.PostAsync(root + "/Groups", GroupContent("Rooted group", members: [Member(userId!)]));
        Assert.True(groupResponse.StatusCode == HttpStatusCode.Created, await groupResponse.Content.ReadAsStringAsync());
        var group = await ReadJsonAsync(groupResponse);
        var groupId = group.GetProperty("id").GetString();
        Assert.Equal(new Uri(client.BaseAddress!, root + "/Users/" + userId).AbsoluteUri,
            Assert.Single(group.GetProperty("members").EnumerateArray()).GetProperty("$ref").GetString());
        using var read = await client.GetAsync(root + "/Users/" + userId);
        var projected = await ReadJsonAsync(read);
        Assert.Equal(new Uri(client.BaseAddress!, root + "/Groups/" + groupId).AbsoluteUri,
            Assert.Single(projected.GetProperty("groups").EnumerateArray()).GetProperty("$ref").GetString());

        using (var groupRead = await client.GetAsync(root + "/Groups/" + groupId)) {
            Assert.Equal(HttpStatusCode.OK, groupRead.StatusCode);
            Assert.Equal(new Uri(client.BaseAddress!, root + "/Users/" + userId).AbsoluteUri,
                Assert.Single((await ReadJsonAsync(groupRead)).GetProperty("members").EnumerateArray()).GetProperty("$ref").GetString());
        }

        foreach (var (collection, filter, expectedId) in new[] {
            ("Groups", "members.$ref pr", groupId), ("Groups", "members[$ref pr]", groupId),
            ("Users", "groups.$ref pr", userId), ("Users", "groups[$ref pr]", userId),
        }) {
            using var get = await client.GetAsync(root + "/" + collection + "?filter=" + Uri.EscapeDataString(filter));
            Assert.Equal(HttpStatusCode.OK, get.StatusCode);
            var result = await ReadJsonAsync(get);
            Assert.Equal(1, result.GetProperty("totalResults").GetInt32());
            Assert.Equal(expectedId, Assert.Single(result.GetProperty("Resources").EnumerateArray()).GetProperty("id").GetString());
            using var search = await client.PostAsync(root + "/" + collection + "/.search", new StringContent(JsonSerializer.Serialize(new {
                schemas = new[] { ScimConstants.SearchRequestSchema }, filter,
            }), Encoding.UTF8, "application/scim+json"));
            Assert.Equal(HttpStatusCode.OK, search.StatusCode);
            var searched = await ReadJsonAsync(search);
            Assert.Equal(1, searched.GetProperty("totalResults").GetInt32());
            Assert.Equal(expectedId, Assert.Single(searched.GetProperty("Resources").EnumerateArray()).GetProperty("id").GetString());
        }

        using (var removed = await client.PatchAsync(root + "/Groups/" + groupId,
            PatchContent(new Dictionary<string, object?> { ["op"] = "remove", ["path"] = "members[$ref pr]" }))) {
            Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
            Assert.Empty((await ReadJsonAsync(removed)).GetProperty("members").EnumerateArray());
        }
        using (var scope = factory.Services.CreateScope()) {
            Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<IRepository<SchemataUserRole>>().CountAsync(query => query));
        }
        using var after = await client.GetAsync(root + "/Users/" + userId);
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
        Assert.Empty((await ReadJsonAsync(after)).GetProperty("groups").EnumerateArray());
    }

    [Fact]
    public async Task Complete_Lifecycle_Preserves_Identity_And_Projects_ReadOnly_User_Groups() {
        using var factory = new WebAppFactory();
        using var client = CreateClient(factory);
        var first = await CreateUserAsync(client, "Group-User-1", "First User", "user-1");
        var second = await CreateUserAsync(client, "Group-User-2", "Second User", "user-2");
        var firstId = first.GetProperty("id").GetString()!;
        var secondId = second.GetProperty("id").GetString()!;

        var created = await CreateGroupAsync(client, "Initial Group", "group-external",
            Member(firstId, reference: $"https://ref.invalid/scim/v2/Users/{firstId}"));
        var id = created.GetProperty("id").GetString()!;
        Assert.Equal("group-external", created.GetProperty("externalId").GetString());
        var initialMember = Assert.Single(created.GetProperty("members").EnumerateArray());
        Assert.Equal(firstId, initialMember.GetProperty("value").GetString());
        Assert.Equal("First User", initialMember.GetProperty("display").GetString());
        Assert.Equal("User", initialMember.GetProperty("type").GetString());
        Assert.Equal(new Uri(client.BaseAddress!, $"/scim/v2/Users/{firstId}").AbsoluteUri, initialMember.GetProperty("$ref").GetString());

        string canonicalName;
        string resourceName;
        using (var scope = factory.Services.CreateScope()) {
            var role = await scope.ServiceProvider.GetRequiredService<IRepository<SchemataRole>>().FindAsync([Guid.Parse(id)]);
            Assert.NotNull(role);
            Assert.Equal(id, role.Uid.ToString());
            Assert.NotNull(role.CanonicalName);
            Assert.NotNull(role.Name);
            canonicalName = role.CanonicalName;
            resourceName = role.Name;
            Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<IRepository<SchemataRoleClaim>>().CountAsync(q => q));
        }

        using (var user = await client.GetAsync($"{Users}/{firstId}")) {
            var group = Assert.Single((await ReadJsonAsync(user)).GetProperty("groups").EnumerateArray());
            Assert.Equal(id, group.GetProperty("value").GetString());
            Assert.Equal("Initial Group", group.GetProperty("display").GetString());
            Assert.Equal(new Uri(client.BaseAddress!, $"/scim/v2/Groups/{id}").AbsoluteUri, group.GetProperty("$ref").GetString());
        }

        using (var readOnly = await client.PatchAsync($"{Users}/{firstId}", PatchContent(
            Operation("replace", "groups", new[] { Member(secondId) })))) {
            Assert.Equal(HttpStatusCode.BadRequest, readOnly.StatusCode);
            AssertScimError(await ReadJsonAsync(readOnly), "400", ScimConstants.Mutability);
        }

        using (var replaced = await client.PutAsync($"{Groups}/{id}", GroupContent("Renamed Group", "group-external",
            Member(secondId)))) {
            Assert.True(replaced.StatusCode == HttpStatusCode.OK, await replaced.Content.ReadAsStringAsync());
            var body = await ReadJsonAsync(replaced);
            Assert.Equal(id, body.GetProperty("id").GetString());
            Assert.Equal(secondId, Assert.Single(body.GetProperty("members").EnumerateArray()).GetProperty("value").GetString());
        }

        using (var scope = factory.Services.CreateScope()) {
            var role = await scope.ServiceProvider.GetRequiredService<IRepository<SchemataRole>>().FindAsync([Guid.Parse(id)]);
            Assert.NotNull(role);
            Assert.Equal(canonicalName, role.CanonicalName);
            Assert.Equal(resourceName, role.Name);
            Assert.Equal("Renamed Group", role.DisplayName);
        }

        using (var formerMember = await client.GetAsync($"{Users}/{firstId}")) {
            Assert.Empty((await ReadJsonAsync(formerMember)).GetProperty("groups").EnumerateArray());
        }
        using (var currentMember = await client.GetAsync($"{Users}/{secondId}")) {
            Assert.Equal(id, Assert.Single((await ReadJsonAsync(currentMember)).GetProperty("groups").EnumerateArray())
                .GetProperty("value").GetString());
        }

        using (var emptied = await client.PutAsync($"{Groups}/{id}", GroupContent("Empty Group"))) {
            Assert.Equal(HttpStatusCode.OK, emptied.StatusCode);
            var body = await ReadJsonAsync(emptied);
            Assert.Empty(body.GetProperty("members").EnumerateArray());
            Assert.False(body.TryGetProperty("externalId", out _));
        }

        using (var deleted = await client.DeleteAsync($"{Groups}/{id}")) {
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        }
        using (var missing = await client.GetAsync($"{Groups}/{id}")) {
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }

        using var verification = factory.Services.CreateScope();
        Assert.Equal(0, await verification.ServiceProvider.GetRequiredService<IRepository<SchemataUserRole>>().CountAsync(q => q));
        Assert.Equal(0, await verification.ServiceProvider.GetRequiredService<IRepository<ScimProvisioningBinding>>()
            .CountAsync(q => q.Where(binding => binding.ResourceType == "Group")));
    }

    [Fact]
    public async Task Create_Requires_Explicit_Role_Provisioning_Privilege() {
        using var factory = new WebAppFactory("PrivilegeDenied");
        using var client = CreateClient(factory);
        var user = await CreateUserAsync(client, "Privilege-User");

        using var response = await client.PostAsync(Groups, GroupContent("Denied Group", members: Member(user.GetProperty("id").GetString()!)));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        AssertScimError(await ReadJsonAsync(response), "403");

        using var scope = factory.Services.CreateScope();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<IRepository<SchemataRole>>().CountAsync(q => q));
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<IRepository<ScimProvisioningBinding>>()
            .CountAsync(q => q.Where(binding => binding.ResourceType == "Group")));
    }

    [Fact]
    public async Task Unbound_Roles_Cannot_Be_Adopted_By_Id_Or_DisplayName() {
        using var factory = new WebAppFactory();
        using var client = CreateClient(factory);
        Guid id;
        string canonicalName;
        using (var scope = factory.Services.CreateScope()) {
            var manager = scope.ServiceProvider.GetRequiredService<RoleManager<SchemataRole>>();
            var role = new SchemataRole { DisplayName = "Privileged Role" };
            Assert.True((await manager.CreateAsync(role)).Succeeded);
            id = role.Uid;
            Assert.NotNull(role.CanonicalName);
            canonicalName = role.CanonicalName;
        }

        using (var list = await client.GetAsync(Groups)) {
            Assert.Equal(0, (await ReadJsonAsync(list)).GetProperty("totalResults").GetInt32());
        }
        using (var get = await client.GetAsync($"{Groups}/{id}")) {
            Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        }
        using (var patch = await client.PatchAsync($"{Groups}/{id}", PatchContent(Operation("replace", "displayName", "Adopted")))) {
            Assert.Equal(HttpStatusCode.Forbidden, patch.StatusCode);
        }
        using (var put = await client.PutAsync($"{Groups}/{id}", GroupContent("Adopted"))) {
            Assert.Equal(HttpStatusCode.Forbidden, put.StatusCode);
        }
        using (var delete = await client.DeleteAsync($"{Groups}/{id}")) {
            Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
        }
        using (var duplicate = await client.PostAsync(Groups, GroupContent("Privileged Role"))) {
            Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        }

        using var verification = factory.Services.CreateScope();
        var stored = await verification.ServiceProvider.GetRequiredService<IRepository<SchemataRole>>().FindAsync([id]);
        Assert.NotNull(stored);
        Assert.Equal("Privileged Role", stored.DisplayName);
        Assert.Equal(canonicalName, stored.CanonicalName);
        Assert.Equal(0, await verification.ServiceProvider.GetRequiredService<IRepository<ScimProvisioningBinding>>()
            .CountAsync(q => q.Where(binding => binding.ResourceType == "Group")));
    }

    [Fact]
    public async Task Patch_Add_Remove_Replace_And_ValuePath_Preserve_Membership_Idempotence() {
        using var factory = new WebAppFactory();
        using var client = CreateClient(factory);
        var first = await CreateUserAsync(client, "Patch-User-1");
        var second = await CreateUserAsync(client, "Patch-User-2");
        var third = await CreateUserAsync(client, "Patch-User-3");
        var firstId = first.GetProperty("id").GetString()!;
        var secondId = second.GetProperty("id").GetString()!;
        var thirdId = third.GetProperty("id").GetString()!;
        var group = await CreateGroupAsync(client, "Patch Group", members: Member(firstId));
        var id = group.GetProperty("id").GetString()!;

        using (var added = await client.PatchAsync($"{Groups}/{id}", PatchContent(
            Operation("add", "members", new[] { Member(secondId), Member(secondId) }),
            Operation("add", "members", Member(secondId))))) {
            Assert.Equal(HttpStatusCode.OK, added.StatusCode);
            var members = (await ReadJsonAsync(added)).GetProperty("members").EnumerateArray().ToList();
            Assert.Equal(2, members.Count);
            Assert.Equal(new[] { firstId, secondId }.OrderBy(value => value), members.Select(member => member.GetProperty("value").GetString()).OrderBy(value => value));
        }

        using (var removed = await client.PatchAsync($"{Groups}/{id}", PatchContent(
            new Dictionary<string, object?> { ["op"] = "remove", ["path"] = $"members[value eq \"{firstId}\"]" }))) {
            Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
            Assert.Equal(secondId, Assert.Single((await ReadJsonAsync(removed)).GetProperty("members").EnumerateArray())
                .GetProperty("value").GetString());
        }

        using (var replaced = await client.PatchAsync($"{Groups}/{id}", PatchContent(
            Operation("replace", "members", new[] { Member(thirdId) }),
            Operation("replace", "displayName", "Patched Group")))) {
            Assert.Equal(HttpStatusCode.OK, replaced.StatusCode);
            var body = await ReadJsonAsync(replaced);
            Assert.Equal("Patched Group", body.GetProperty("displayName").GetString());
            Assert.Equal(thirdId, Assert.Single(body.GetProperty("members").EnumerateArray()).GetProperty("value").GetString());
        }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("cross-domain")]
    [InlineData("bad-value")]
    [InlineData("bad-type")]
    [InlineData("bad-ref-value")]
    [InlineData("bad-ref-type")]
    [InlineData("bad-ref-syntax")]
    public async Task Invalid_Members_Are_Rejected_Without_Partial_Group_Writes(string scenario) {
        using var factory = new WebAppFactory();
        using var owner = CreateClient(factory);
        using var other = CreateClient(factory, "domain-b");
        var owned = await CreateUserAsync(owner, "Owned-Member");
        var foreign = await CreateUserAsync(other, "Foreign-Member");
        var ownedId = owned.GetProperty("id").GetString()!;
        var foreignId = foreign.GetProperty("id").GetString()!;
        var member = scenario switch {
            "missing" => Member(Guid.NewGuid().ToString()),
            "cross-domain" => Member(foreignId),
            "bad-value" => Member("not-a-guid"),
            "bad-type" => Member(ownedId, "Group"),
            "bad-ref-value" => Member(ownedId, reference: $"/scim/v2/Users/{Guid.NewGuid()}"),
            "bad-ref-type" => Member(ownedId, reference: $"/scim/v2/Groups/{ownedId}"),
            "bad-ref-syntax" => Member(ownedId, reference: "://not-a-uri"),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };

        using var response = await owner.PostAsync(Groups, GroupContent($"Invalid {scenario}", members: member));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertScimError(await ReadJsonAsync(response), "400", ScimConstants.InvalidValue);

        using var scope = factory.Services.CreateScope();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<IRepository<SchemataRole>>().CountAsync(q => q));
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<IRepository<SchemataUserRole>>().CountAsync(q => q));
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<IRepository<ScimProvisioningBinding>>()
            .CountAsync(q => q.Where(binding => binding.ResourceType == "Group")));
    }

    [Fact]
    public async Task Nested_Groups_Are_Rejected_Without_Changing_The_Existing_Group() {
        using var factory = new WebAppFactory();
        using var client = CreateClient(factory);
        var existing = await CreateGroupAsync(client, "Existing Group");
        var id = existing.GetProperty("id").GetString()!;

        using var response = await client.PostAsync(Groups, GroupContent("Nested Group",
            members: Member(id, "Group", $"/scim/v2/Groups/{id}")));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertScimError(await ReadJsonAsync(response), "400", ScimConstants.InvalidValue);

        using var list = await client.GetAsync(Groups);
        var root = await ReadJsonAsync(list);
        Assert.Equal(1, root.GetProperty("totalResults").GetInt32());
        Assert.Equal(id, Assert.Single(root.GetProperty("Resources").EnumerateArray()).GetProperty("id").GetString());
    }

    [Fact]
    public async Task Cross_Domain_Group_Writes_Are_Denied_And_Reads_Are_Not_Listed() {
        using var factory = new WebAppFactory();
        using var owner = CreateClient(factory);
        using var other = CreateClient(factory, "domain-b");
        var created = await CreateGroupAsync(owner, "Owned Group", "owned-group");
        var id = created.GetProperty("id").GetString()!;

        using (var list = await other.GetAsync(Groups)) {
            Assert.Equal(0, (await ReadJsonAsync(list)).GetProperty("totalResults").GetInt32());
        }
        using (var get = await other.GetAsync($"{Groups}/{id}")) {
            Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        }
        using (var patch = await other.PatchAsync($"{Groups}/{id}", PatchContent(Operation("replace", "displayName", "Stolen")))) {
            Assert.Equal(HttpStatusCode.Forbidden, patch.StatusCode);
        }
        using (var put = await other.PutAsync($"{Groups}/{id}", GroupContent("Stolen"))) {
            Assert.Equal(HttpStatusCode.Forbidden, put.StatusCode);
        }
        using (var delete = await other.DeleteAsync($"{Groups}/{id}")) {
            Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
        }

        using var owned = await owner.GetAsync($"{Groups}/{id}");
        Assert.Equal("Owned Group", (await ReadJsonAsync(owned)).GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task Mid_Patch_Failure_Restores_Role_Members_And_Binding() {
        using var factory = new WebAppFactory("MembershipFailure");
        using var client = CreateClient(factory);
        var first = await CreateUserAsync(client, "Rollback-User-1");
        var second = await CreateUserAsync(client, "Rollback-User-2");
        var third = await CreateUserAsync(client, "Rollback-User-3");
        var group = await CreateGroupAsync(client, "Original Group", "before-binding",
            members: Member(first.GetProperty("id").GetString()!));
        var id = group.GetProperty("id").GetString()!;
        var before = await ReadGroupSnapshotAsync(factory, id);

        using var response = await client.PatchAsync($"{Groups}/{id}", PatchContent(
            Operation("replace", "displayName", "Changed Group"),
            Operation("replace", "externalId", "after-binding"),
            Operation("add", "members", new[] {
                Member(second.GetProperty("id").GetString()!),
                Member(third.GetProperty("id").GetString()!),
            })));
        Assert.True(response.StatusCode == HttpStatusCode.InternalServerError, await response.Content.ReadAsStringAsync());

        var after = await ReadGroupSnapshotAsync(factory, id);
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task Missing_Role_Store_Capability_Fails_Before_Staging() {
        using var factory = new WebAppFactory("MissingRoleCapability");
        using var client = CreateClient(factory);

        using var response = await client.PostAsync(Groups, GroupContent("Unsupported Group"));
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        AssertScimError(await ReadJsonAsync(response), "500");

        using var scope = factory.Services.CreateScope();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<IRepository<SchemataRole>>().CountAsync(q => q));
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<IRepository<ScimProvisioningBinding>>()
            .CountAsync(q => q.Where(binding => binding.ResourceType == "Group")));
    }

    private static async Task<GroupSnapshot> ReadGroupSnapshotAsync(WebAppFactory factory, string id) {
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var role = await services.GetRequiredService<IRepository<SchemataRole>>().FindAsync([Guid.Parse(id)]);
        Assert.NotNull(role);
        Assert.NotNull(role.CanonicalName);
        Assert.NotNull(role.Name);
        var binding = Assert.Single(await services.GetRequiredService<IRepository<ScimProvisioningBinding>>()
            .ListAsync(q => q.Where(item => item.ResourceType == "Group" && item.EntityUid == role.Uid)).ToListAsync());
        var members = await services.GetRequiredService<IRepository<SchemataUserRole>>()
            .ListAsync(q => q.Where(link => link.RoleId == role.CanonicalName).OrderBy(link => link.UserId)).ToListAsync();
        return new(role.DisplayName ?? string.Empty, role.NormalizedName, role.Name, role.CanonicalName, binding.ExternalId,
            string.Join(",", members.Select(member => member.UserId)));
    }

    private static HttpClient CreateClient(WebAppFactory factory, string domain = "domain-a") {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Scim", "1");
        client.DefaultRequestHeaders.Add("X-Test-Domain", domain);
        return client;
    }

    private static async Task<JsonElement> CreateUserAsync(HttpClient client, string userName, string? displayName = null,
        string? externalId = null) {
        var payload = new Dictionary<string, object?> {
            ["schemas"] = new[] { ScimConstants.UserSchema },
            ["userName"] = userName,
            ["displayName"] = displayName,
            ["externalId"] = externalId ?? $"external-{userName}",
        };
        using var response = await client.PostAsync(Users, JsonContent(payload));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadJsonAsync(response);
    }

    private static async Task<JsonElement> CreateGroupAsync(HttpClient client, string displayName, string? externalId = null,
        params object[] members) {
        using var response = await client.PostAsync(Groups, GroupContent(displayName, externalId, members));
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await ReadJsonAsync(response);
    }

    private static StringContent GroupContent(string displayName, string? externalId = null, params object[] members) {
        var payload = new Dictionary<string, object?> {
            ["schemas"] = new[] { ScimConstants.GroupSchema },
            ["displayName"] = displayName,
        };
        if (externalId is not null) {
            payload["externalId"] = externalId;
        }
        if (members.Length > 0) {
            payload["members"] = members;
        }
        return JsonContent(payload);
    }

    private static Dictionary<string, object?> Member(string value, string? type = "User", string? reference = null) {
        var member = new Dictionary<string, object?> {
            ["value"] = value,
            ["type"] = type,
        };
        if (reference is not null) {
            member["$ref"] = reference;
        }
        return member;
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
    }

    private sealed record GroupSnapshot(
        string DisplayName,
        string? NormalizedName,
        string ResourceName,
        string CanonicalName,
        string? ExternalId,
        string Members);
}
