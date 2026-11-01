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
using Schemata.Abstractions;
using Schemata.Entity.Repository;
using Schemata.Identity.Scim.Integration.Tests.Fixtures;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Managers;
using Schemata.Scim;
using Xunit;

namespace Schemata.Identity.Scim.Integration.Tests;

[Trait("Layer", "Integration")]
public sealed class ScimGroupsShould
{
    private const string Users  = "/scim/v2/Users";
    private const string Groups = "/scim/v2/Groups";

    [Fact]
    public async Task Create_Get_List_Replace_And_Delete_A_Group() {
        using var factory = new WebAppFactory();
        using var client  = CreateClient(factory);
        var       alice   = await CreateUserAsync(client, "Alice", "Alice Example");
        var       bob     = await CreateUserAsync(client, "Bob", "Bob Example");
        var       aliceId = alice.GetProperty("id").GetString()!;
        var       bobId   = bob.GetProperty("id").GetString()!;

        using var created = await client.PostAsync(
            Groups,
            GroupContent("Engineering", Member(aliceId), Member(bobId)));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(ScimConstants.MediaType, created.Content.Headers.ContentType?.MediaType);
        var body = await ReadJsonAsync(created);
        var id   = body.GetProperty("id").GetString();
        Assert.NotNull(id);
        Assert.True(Guid.TryParse(id, out _));
        Assert.Equal(ScimConstants.GroupSchema, body.GetProperty("schemas")[0].GetString());
        Assert.Equal("Engineering", body.GetProperty("displayName").GetString());
        Assert.Equal("Group", body.GetProperty("meta").GetProperty("resourceType").GetString());
        Assert.EndsWith($"{Groups}/{id}", body.GetProperty("meta").GetProperty("location").GetString());
        Assert.Equal(body.GetProperty("meta").GetProperty("location").GetString(), created.Headers.Location?.ToString());

        var members = body.GetProperty("members").EnumerateArray().ToList();
        Assert.Equal(2, members.Count);
        Assert.All(members, member => {
            Assert.Equal("User", member.GetProperty("type").GetString());
            Assert.EndsWith($"{Users}/{member.GetProperty("value").GetString()}",
                            member.GetProperty("$ref").GetString());
        });
        Assert.Contains(members, member => member.GetProperty("value").GetString() == aliceId
                                        && member.GetProperty("display").GetString() == "Alice Example");
        Assert.Contains(members, member => member.GetProperty("value").GetString() == bobId
                                        && member.GetProperty("display").GetString() == "Bob Example");

        using (var single = await client.GetAsync($"{Groups}/{id}")) {
            Assert.Equal(HttpStatusCode.OK, single.StatusCode);
            var root = await ReadJsonAsync(single);
            Assert.Equal(id, root.GetProperty("id").GetString());
            Assert.Equal("Engineering", root.GetProperty("displayName").GetString());
            Assert.Equal(2, root.GetProperty("members").GetArrayLength());
        }

        using (var list = await client.GetAsync(Groups)) {
            var root = await ReadJsonAsync(list);
            Assert.Equal(ScimConstants.ListResponseSchema, root.GetProperty("schemas")[0].GetString());
            Assert.Equal(1, root.GetProperty("totalResults").GetInt32());
            Assert.Equal(1, root.GetProperty("startIndex").GetInt32());
            Assert.Equal(1, root.GetProperty("itemsPerPage").GetInt32());
            Assert.Equal(id, root.GetProperty("Resources")[0].GetProperty("id").GetString());
        }

        using (var replaced = await client.PutAsync($"{Groups}/{id}", GroupContent("Engineering-Renamed", Member(bobId)))) {
            Assert.Equal(HttpStatusCode.OK, replaced.StatusCode);
            var root = await ReadJsonAsync(replaced);
            Assert.Equal(id, root.GetProperty("id").GetString());
            Assert.Equal("Engineering-Renamed", root.GetProperty("displayName").GetString());
            Assert.Equal(1, root.GetProperty("members").GetArrayLength());
            Assert.Equal(bobId, root.GetProperty("members")[0].GetProperty("value").GetString());
        }

        using (var single = await client.GetAsync($"{Groups}/{id}")) {
            var root = await ReadJsonAsync(single);
            Assert.Equal("Engineering-Renamed", root.GetProperty("displayName").GetString());
            Assert.Equal(1, root.GetProperty("members").GetArrayLength());
            Assert.Equal(bobId, root.GetProperty("members")[0].GetProperty("value").GetString());
        }

        using (var bobView = await client.GetAsync($"{Users}/{bobId}")) {
            var groups = (await ReadJsonAsync(bobView)).GetProperty("groups");
            Assert.Equal(1, groups.GetArrayLength());
            var group = groups[0];
            Assert.Equal(id, group.GetProperty("value").GetString());
            Assert.Equal("Engineering-Renamed", group.GetProperty("display").GetString());
            Assert.Equal("direct", group.GetProperty("type").GetString());
            Assert.EndsWith($"{Groups}/{id}", group.GetProperty("$ref").GetString());
        }

        using (var scope = factory.Services.CreateScope()) {
            var users = scope.ServiceProvider.GetRequiredService<SchemataUserManager<SchemataUser>>();
            var a     = await users.FindByNameAsync("Alice");
            var b     = await users.FindByNameAsync("Bob");
            Assert.NotNull(a);
            Assert.NotNull(b);
            Assert.False(await users.IsInRoleAsync(a!, "Engineering-Renamed"));
            Assert.True(await users.IsInRoleAsync(b!, "Engineering-Renamed"));
        }

        using (var deleted = await client.DeleteAsync($"{Groups}/{id}")) {
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        }

        using (var missing = await client.GetAsync($"{Groups}/{id}")) {
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            AssertScimError(await ReadJsonAsync(missing), "404");
        }

        using (var list = await client.GetAsync(Groups)) {
            var root = await ReadJsonAsync(list);
            Assert.Equal(0, root.GetProperty("totalResults").GetInt32());
            Assert.Equal(0, root.GetProperty("Resources").GetArrayLength());
        }

        using (var bobView = await client.GetAsync($"{Users}/{bobId}")) {
            Assert.Equal(0, (await ReadJsonAsync(bobView)).GetProperty("groups").GetArrayLength());
        }

        using (var scope = factory.Services.CreateScope()) {
            Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<IRepository<SchemataRole>>()
                                           .CountAsync(q => q));
            Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<IRepository<SchemataUserRole>>()
                                           .CountAsync(q => q));
            Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<IRepository<SchemataRoleClaim>>()
                                           .CountAsync(q => q));
        }
    }

    [Fact]
    public async Task Require_A_Display_Name() {
        using var factory = new WebAppFactory();
        using var client  = CreateClient(factory);

        using (var missing = await client.PostAsync(Groups, GroupContent(null))) {
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
            AssertScimError(await ReadJsonAsync(missing), "400", ScimConstants.InvalidValue);
        }

        using (var blank = await client.PostAsync(Groups, GroupContent("   "))) {
            Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
            AssertScimError(await ReadJsonAsync(blank), "400", ScimConstants.InvalidValue);
        }

        var group = await CreateGroupAsync(client, GroupContent("Required"));
        var id    = group.GetProperty("id").GetString()!;

        using (var blank = await client.PutAsync($"{Groups}/{id}", GroupContent("   "))) {
            Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
            AssertScimError(await ReadJsonAsync(blank), "400", ScimConstants.InvalidValue);
        }

        using (var single = await client.GetAsync($"{Groups}/{id}")) {
            Assert.Equal("Required", (await ReadJsonAsync(single)).GetProperty("displayName").GetString());
        }
    }

    [Fact]
    public async Task Validate_Member_Value_Reference_And_Type() {
        using var factory = new WebAppFactory();
        using var client  = CreateClient(factory);
        var       alice   = await CreateUserAsync(client, "Alice", "Alice Example");
        var       aliceId = alice.GetProperty("id").GetString()!;
        var       otherId = Guid.NewGuid().ToString();

        foreach (var invalid in new[] {
                     GroupContent("Mismatched", Member(aliceId, $"{Users}/{otherId}")),
                     GroupContent("Group-Type", Member(aliceId, type: "Group")),
                     GroupContent("Nested", Member(aliceId, $"{Groups}/{aliceId}")),
                     GroupContent("Missing", Member(otherId)),
                     GroupContent("No-Value", new Dictionary<string, object?> { ["type"] = "User" }),
                 }) {
            using var response = await client.PostAsync(Groups, invalid);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            AssertScimError(await ReadJsonAsync(response), "400", ScimConstants.InvalidValue);
        }

        var payload = new Dictionary<string, object?> {
            ["id"]          = "forged",
            ["schemas"]     = new[] { ScimConstants.GroupSchema },
            ["displayName"] = "Validated",
            ["members"] = new[] {
                Member(aliceId, display: "Forged Display"),
                Member(aliceId),
            },
            ["meta"] = new Dictionary<string, object?>(),
        };

        using (var response = await client.PostAsync(Groups, JsonContent(payload))) {
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var root = await ReadJsonAsync(response);
            Assert.NotEqual("forged", root.GetProperty("id").GetString());
            Assert.True(Guid.TryParse(root.GetProperty("id").GetString(), out _));
            Assert.Equal(1, root.GetProperty("members").GetArrayLength());
            var member = root.GetProperty("members")[0];
            Assert.Equal(aliceId, member.GetProperty("value").GetString());
            Assert.Equal("Alice Example", member.GetProperty("display").GetString());
            Assert.EndsWith($"{Users}/{aliceId}", member.GetProperty("$ref").GetString());
        }

        using var list = await client.GetAsync(Groups);
        Assert.Equal(1, (await ReadJsonAsync(list)).GetProperty("totalResults").GetInt32());
    }

    [Fact]
    public async Task Replace_Clears_The_Complete_Membership() {
        using var factory = new WebAppFactory();
        using var client  = CreateClient(factory);
        var       alice   = await CreateUserAsync(client, "Alice");
        var       bob     = await CreateUserAsync(client, "Bob");
        var       aliceId = alice.GetProperty("id").GetString()!;
        var       bobId   = bob.GetProperty("id").GetString()!;

        var group = await CreateGroupAsync(client, GroupContent("Clearing", Member(aliceId), Member(bobId)));
        var id    = group.GetProperty("id").GetString()!;

        using (var cleared = await client.PutAsync($"{Groups}/{id}", GroupContent("Clearing", Array.Empty<object>()))) {
            Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
            Assert.Equal(0, (await ReadJsonAsync(cleared)).GetProperty("members").GetArrayLength());
        }

        using (var scope = factory.Services.CreateScope()) {
            Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<IRepository<SchemataUserRole>>()
                                           .CountAsync(q => q));
        }

        using (var added = await client.PutAsync($"{Groups}/{id}", GroupContent("Clearing", Member(aliceId)))) {
            Assert.Equal(HttpStatusCode.OK, added.StatusCode);
            Assert.Equal(1, (await ReadJsonAsync(added)).GetProperty("members").GetArrayLength());
        }

        using (var omitted = await client.PutAsync($"{Groups}/{id}", GroupContent("Clearing"))) {
            Assert.Equal(HttpStatusCode.OK, omitted.StatusCode);
            Assert.Equal(0, (await ReadJsonAsync(omitted)).GetProperty("members").GetArrayLength());
        }

        using (var scope = factory.Services.CreateScope()) {
            Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<IRepository<SchemataUserRole>>()
                                           .CountAsync(q => q));
        }
    }

    [Fact]
    public async Task Roll_Back_A_Failed_Membership_Replacement() {
        using var factory = new WebAppFactory("MembershipFailure");
        using var client  = CreateClient(factory);
        var       alice   = await CreateUserAsync(client, "Alice");
        var       aliceId = alice.GetProperty("id").GetString()!;

        string failingId;
        using (var scope = factory.Services.CreateScope()) {
            var repository = scope.ServiceProvider.GetRequiredService<IRepository<SchemataUser>>();
            var user       = new SchemataUser { UserName = "Failing", Name = "failing-member", DisplayName = "Failing" };
            await repository.AddAsync(user);
            await repository.CommitAsync();
            failingId = user.Uid.ToString();
        }

        var group = await CreateGroupAsync(client, GroupContent("Rollback", Member(aliceId)));
        var id    = group.GetProperty("id").GetString()!;

        using (var failed = await client.PutAsync(
                   $"{Groups}/{id}",
                   GroupContent("Rollback-Renamed", Member(failingId)))) {
            Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
            AssertScimError(await ReadJsonAsync(failed), "500");
        }

        using (var single = await client.GetAsync($"{Groups}/{id}")) {
            var root = await ReadJsonAsync(single);
            Assert.Equal("Rollback", root.GetProperty("displayName").GetString());
            Assert.Equal(1, root.GetProperty("members").GetArrayLength());
            Assert.Equal(aliceId, root.GetProperty("members")[0].GetProperty("value").GetString());
        }

        using (var scope = factory.Services.CreateScope()) {
            var role = await scope.ServiceProvider.GetRequiredService<IRepository<SchemataRole>>()
                                    .SingleOrDefaultAsync(q => q.Where(r => r.Uid == Guid.Parse(id)));
            Assert.NotNull(role);
            Assert.Equal("Rollback", role!.DisplayName);
            var links = await scope.ServiceProvider.GetRequiredService<IRepository<SchemataUserRole>>()
                                    .ListAsync(q => q.Where(l => l.RoleId == role.CanonicalName))
                                    .ToListAsync();
            Assert.Single(links);
            Assert.DoesNotContain(links, link => link.UserId == FailingMembershipAdvisor.User);
        }
    }

    [Fact]
    public async Task Reject_Unauthenticated_And_Unauthorized_Group_Requests() {
        using (var factory = new WebAppFactory()) {
            using var anonymous = CreateClient(factory, authenticated: false);
            using var get       = await anonymous.GetAsync(Groups);
            Assert.Equal(HttpStatusCode.Unauthorized, get.StatusCode);
            AssertScimError(await ReadJsonAsync(get), "401");
            using var post = await anonymous.PostAsync(Groups, GroupContent("Anonymous"));
            Assert.Equal(HttpStatusCode.Unauthorized, post.StatusCode);
            AssertScimError(await ReadJsonAsync(post), "401");
        }

        using (var factory = new WebAppFactory("Denied")) {
            using var client = CreateClient(factory);
            using var denied = await client.GetAsync(Groups);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            AssertScimError(await ReadJsonAsync(denied), "403");
            using var post = await client.PostAsync(Groups, GroupContent("Denied"));
            Assert.Equal(HttpStatusCode.Forbidden, post.StatusCode);
            AssertScimError(await ReadJsonAsync(post), "403");
        }

        using (var factory = new WebAppFactory("ScimPolicy")) {
            using var client = CreateClient(factory);
            using var denied = await client.GetAsync(Groups);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            AssertScimError(await ReadJsonAsync(denied), "403");
        }
    }

    [Fact]
    public async Task Do_Not_Expose_Or_Adopt_Unmanaged_Roles() {
        using var factory     = new WebAppFactory();
        using var client      = CreateClient(factory);
        string    privilegedId;
        string    privilegedName;
        using (var scope = factory.Services.CreateScope()) {
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<SchemataRole>>();
            var role  = new SchemataRole { DisplayName = "Privileged" };
            var result = await roles.CreateAsync(role);
            Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Description)));
            privilegedId   = role.Uid.ToString();
            privilegedName = role.Name!;
        }

        using (var list = await client.GetAsync(Groups)) {
            Assert.Equal(0, (await ReadJsonAsync(list)).GetProperty("totalResults").GetInt32());
        }

        using (var single = await client.GetAsync($"{Groups}/{privilegedId}")) {
            Assert.Equal(HttpStatusCode.NotFound, single.StatusCode);
            AssertScimError(await ReadJsonAsync(single), "404");
        }

        using (var named = await client.GetAsync($"{Groups}/{privilegedName}")) {
            Assert.Equal(HttpStatusCode.NotFound, named.StatusCode);
        }

        using (var replaced = await client.PutAsync($"{Groups}/{privilegedId}", GroupContent("Hijacked"))) {
            Assert.Equal(HttpStatusCode.NotFound, replaced.StatusCode);
        }

        using (var deleted = await client.DeleteAsync($"{Groups}/{privilegedId}")) {
            Assert.Equal(HttpStatusCode.NotFound, deleted.StatusCode);
        }

        using (var duplicate = await client.PostAsync(Groups, GroupContent("Privileged"))) {
            Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
            AssertScimError(await ReadJsonAsync(duplicate), "409", ScimConstants.Uniqueness);
        }

        using (var scope = factory.Services.CreateScope()) {
            var roles = await scope.ServiceProvider.GetRequiredService<IRepository<SchemataRole>>()
                                    .ListAsync(q => q)
                                    .ToListAsync();
            Assert.Single(roles);
            Assert.Equal(privilegedId, roles[0].Uid.ToString());
        }

        var allowed = await CreateGroupAsync(client, GroupContent("Allowed"));
        Assert.NotEqual(privilegedId, allowed.GetProperty("id").GetString());

        using (var list = await client.GetAsync(Groups)) {
            var resources = (await ReadJsonAsync(list)).GetProperty("Resources").EnumerateArray().ToList();
            Assert.Single(resources);
            Assert.Equal(allowed.GetProperty("id").GetString(), resources[0].GetProperty("id").GetString());
        }
    }

    [Fact]
    public async Task Page_List_Groups_With_Clamped_Start_Index_And_Count() {
        using var factory = new WebAppFactory("Limited");
        using var client  = CreateClient(factory);
        foreach (var name in new[] { "Page-A", "Page-B", "Page-C" }) {
            await CreateGroupAsync(client, GroupContent(name));
        }

        using (var response = await client.GetAsync($"{Groups}?startIndex=2&count=1")) {
            var root = await ReadJsonAsync(response);
            Assert.Equal(3, root.GetProperty("totalResults").GetInt32());
            Assert.Equal(2, root.GetProperty("startIndex").GetInt32());
            Assert.Equal(1, root.GetProperty("itemsPerPage").GetInt32());
            Assert.Equal(1, root.GetProperty("Resources").GetArrayLength());
        }

        using (var response = await client.GetAsync($"{Groups}?startIndex=0&count=10")) {
            var root = await ReadJsonAsync(response);
            Assert.Equal(1, root.GetProperty("startIndex").GetInt32());
            Assert.Equal(2, root.GetProperty("itemsPerPage").GetInt32());
            Assert.Equal(2, root.GetProperty("Resources").GetArrayLength());
        }

        using (var response = await client.GetAsync($"{Groups}?startIndex=1&count=-5")) {
            var root = await ReadJsonAsync(response);
            Assert.Equal(3, root.GetProperty("totalResults").GetInt32());
            Assert.Equal(0, root.GetProperty("itemsPerPage").GetInt32());
            Assert.Equal(0, root.GetProperty("Resources").GetArrayLength());
        }

        using (var response = await client.GetAsync($"{Groups}?startIndex=99")) {
            var root = await ReadJsonAsync(response);
            Assert.Equal(99, root.GetProperty("startIndex").GetInt32());
            Assert.Equal(0, root.GetProperty("itemsPerPage").GetInt32());
            Assert.Equal(0, root.GetProperty("Resources").GetArrayLength());
        }
    }

    [Fact]
    public async Task Reject_Group_Filters_And_Report_Patch_As_Not_Implemented() {
        using var factory = new WebAppFactory();
        using var client  = CreateClient(factory);
        var       group   = await CreateGroupAsync(client, GroupContent("Filtered"));
        var       id      = group.GetProperty("id").GetString();

        using (var filtered = await client.GetAsync($"{Groups}?filter={Uri.EscapeDataString("displayName eq \"Filtered\"")}")) {
            Assert.Equal(HttpStatusCode.BadRequest, filtered.StatusCode);
            AssertScimError(await ReadJsonAsync(filtered), "400", ScimConstants.InvalidFilter);
        }

        using var patch = await client.PatchAsync(
            $"{Groups}/{id}",
            new StringContent("{\"Operations\":[]}", Encoding.UTF8, ScimConstants.MediaType));
        Assert.Equal(HttpStatusCode.NotImplemented, patch.StatusCode);
        AssertScimError(await ReadJsonAsync(patch), "501");
    }

    [Fact]
    public async Task Reject_A_Duplicate_Display_Name_On_Replace() {
        using var factory = new WebAppFactory();
        using var client  = CreateClient(factory);
        var       first   = await CreateGroupAsync(client, GroupContent("First"));
        await CreateGroupAsync(client, GroupContent("Second"));
        var id = first.GetProperty("id").GetString();

        using (var duplicate = await client.PutAsync($"{Groups}/{id}", GroupContent("Second"))) {
            Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
            AssertScimError(await ReadJsonAsync(duplicate), "409", ScimConstants.Uniqueness);
        }

        using var single = await client.GetAsync($"{Groups}/{id}");
        Assert.Equal("First", (await ReadJsonAsync(single)).GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task Adopt_An_Administratively_Marked_Role() {
        using var factory = new WebAppFactory();
        using var client  = CreateClient(factory);
        string roleId;
        using (var scope = factory.Services.CreateScope()) {
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<SchemataRole>>();
            var role  = new SchemataRole { DisplayName = "Adopted" };
            Assert.True((await roles.CreateAsync(role)).Succeeded);
            var marked = await roles.AddClaimAsync(role, new(SchemataConstants.ProvisioningClaims.ScimGroup, "managed"));
            Assert.True(marked.Succeeded);
            roleId = role.Uid.ToString();
        }

        using (var single = await client.GetAsync($"{Groups}/{roleId}")) {
            Assert.Equal(HttpStatusCode.OK, single.StatusCode);
            var root = await ReadJsonAsync(single);
            Assert.Equal("Adopted", root.GetProperty("displayName").GetString());
            Assert.Equal(0, root.GetProperty("members").GetArrayLength());
        }

        var member = await CreateUserAsync(client, "Adopted-Member");
        var userId = member.GetProperty("id").GetString()!;

        using (var added = await client.PutAsync($"{Groups}/{roleId}", GroupContent("Adopted-Renamed", Member(userId)))) {
            Assert.Equal(HttpStatusCode.OK, added.StatusCode);
            Assert.Equal("Adopted-Renamed", (await ReadJsonAsync(added)).GetProperty("displayName").GetString());
        }

        using (var scope = factory.Services.CreateScope()) {
            Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<IRepository<SchemataUserRole>>()
                                           .CountAsync(q => q));
        }

        using (var cleared = await client.PutAsync($"{Groups}/{roleId}", GroupContent("Adopted-Renamed", Array.Empty<object>()))) {
            Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
            Assert.Equal(0, (await ReadJsonAsync(cleared)).GetProperty("members").GetArrayLength());
        }

        using (var scope = factory.Services.CreateScope()) {
            Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<IRepository<SchemataUserRole>>()
                                           .CountAsync(q => q));
            var marker = await scope.ServiceProvider.GetRequiredService<IRepository<SchemataRoleClaim>>()
                                    .ListAsync(q => q.Where(rc => rc.ClaimType == SchemataConstants.ProvisioningClaims.ScimGroup))
                                    .ToListAsync();
            Assert.Single(marker);
        }

        using (var deleted = await client.DeleteAsync($"{Groups}/{roleId}")) {
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        }

        using (var scope = factory.Services.CreateScope()) {
            Assert.Null(await scope.ServiceProvider.GetRequiredService<IRepository<SchemataRole>>()
                                   .SingleOrDefaultAsync(q => q.Where(r => r.Uid == Guid.Parse(roleId))));
        }
    }

    [Fact]
    public async Task Keep_The_Management_Marker_Out_Of_Principals() {
        using var factory = new WebAppFactory();
        using var client  = CreateClient(factory);
        var       user    = await CreateUserAsync(client, "Marker-User");
        var       userId  = user.GetProperty("id").GetString()!;
        var       group   = await CreateGroupAsync(client, GroupContent("Marker-Group", Member(userId)));
        var       groupId = group.GetProperty("id").GetString()!;

        using (var scope = factory.Services.CreateScope()) {
            var manager = scope.ServiceProvider.GetRequiredService<SchemataUserManager<SchemataUser>>();
            var entity  = await manager.FindByNameAsync("Marker-User");
            Assert.NotNull(entity);
            var principals = scope.ServiceProvider.GetRequiredService<IUserClaimsPrincipalFactory<SchemataUser>>();
            var principal  = await principals.CreateAsync(entity!);
            Assert.DoesNotContain(principal.Claims,
                                  claim => claim.Type == SchemataConstants.ProvisioningClaims.ScimGroup);
            Assert.Contains(principal.Claims,
                            claim => claim.Type == SchemataConstants.IdentityClaims.Role
                                  && claim.Value == "Marker-Group");

            var role = await scope.ServiceProvider.GetRequiredService<IRepository<SchemataRole>>()
                               .SingleOrDefaultAsync(q => q.Where(r => r.Uid == Guid.Parse(groupId)));
            Assert.NotNull(role);
            var markers = await scope.ServiceProvider.GetRequiredService<IRepository<SchemataRoleClaim>>()
                                      .ListAsync(q => q.Where(rc => rc.RoleId == role!.CanonicalName
                                                                 && rc.ClaimType == SchemataConstants.ProvisioningClaims.ScimGroup))
                                      .ToListAsync();
            Assert.Single(markers);
        }
    }

    [Fact]
    public async Task Keep_Groups_Off_A_Custom_Role_Host() {
        using var factory = new WebAppFactory("CustomRole");
        using var client  = CreateClient(factory);
        var       user    = await CreateUserAsync(client, "Custom-Host");
        var       userId  = user.GetProperty("id").GetString()!;

        using (var single = await client.GetAsync($"{Users}/{userId}")) {
            Assert.Equal(HttpStatusCode.OK, single.StatusCode);
        }

        using (var response = await client.GetAsync("/scim/v2/ResourceTypes")) {
            var names = (await ReadJsonAsync(response)).GetProperty("Resources")
                                                       .EnumerateArray()
                                                       .Select(resource => resource.GetProperty("name").GetString())
                                                       .ToList();
            Assert.Contains("User", names);
            Assert.DoesNotContain("Group", names);
        }

        using (var response = await client.GetAsync($"/scim/v2/Schemas/{Uri.EscapeDataString(ScimConstants.GroupSchema)}")) {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        using (var response = await client.GetAsync(Groups)) {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            AssertScimError(await ReadJsonAsync(response), "404");
        }
    }

    [Fact]
    public async Task Register_The_Group_Resource_Type_And_Schema_With_Discovery() {
        using var factory = new WebAppFactory();
        using var client  = CreateClient(factory);

        using (var response = await client.GetAsync("/scim/v2/ResourceTypes")) {
            var group = (await ReadJsonAsync(response)).GetProperty("Resources")
                                                       .EnumerateArray()
                                                       .Single(r => r.GetProperty("name").GetString() == "Group");
            Assert.Equal("/Groups", group.GetProperty("endpoint").GetString());
            Assert.Equal(ScimConstants.GroupSchema, group.GetProperty("schema").GetString());
        }

        using (var response = await client.GetAsync($"/scim/v2/Schemas/{Uri.EscapeDataString(ScimConstants.GroupSchema)}")) {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var attributes = (await ReadJsonAsync(response)).GetProperty("attributes").EnumerateArray().ToList();
            var displayName = attributes.Single(a => a.GetProperty("name").GetString() == "displayName");
            Assert.True(displayName.GetProperty("required").GetBoolean());
            Assert.Equal(ScimConstants.ReadWrite, displayName.GetProperty("mutability").GetString());
            var members = attributes.Single(a => a.GetProperty("name").GetString() == "members");
            Assert.True(members.GetProperty("multiValued").GetBoolean());
            Assert.Equal(ScimConstants.ReadWrite, members.GetProperty("mutability").GetString());
            var subAttributes = members.GetProperty("subAttributes").EnumerateArray().ToList();
            Assert.Equal(ScimConstants.Immutable,
                         subAttributes.Single(a => a.GetProperty("name").GetString() == "value")
                                      .GetProperty("mutability").GetString());
            Assert.Equal(ScimConstants.ReadOnly,
                         subAttributes.Single(a => a.GetProperty("name").GetString() == "display")
                                      .GetProperty("mutability").GetString());
            var types = subAttributes.Single(a => a.GetProperty("name").GetString() == "type")
                                     .GetProperty("canonicalValues")
                                     .EnumerateArray()
                                     .Select(value => value.GetString()!)
                                     .ToArray();
            Assert.Equal(new[] { "User" }, types);
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
        var payload = new Dictionary<string, object?> { ["userName"] = userName };
        if (displayName is not null) {
            payload["displayName"] = displayName;
        }

        using var response = await client.PostAsync(Users, JsonContent(payload));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return await ReadJsonAsync(response);
    }

    private static async Task<JsonElement> CreateGroupAsync(HttpClient client, StringContent content) {
        using var response = await client.PostAsync(Groups, content);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return await ReadJsonAsync(response);
    }

    private static StringContent GroupContent(string? displayName, params object[] members) {
        var payload = new Dictionary<string, object?>();
        if (displayName is not null) {
            payload["displayName"] = displayName;
        }

        if (members.Length > 0) {
            payload["members"] = members;
        }

        return JsonContent(payload);
    }

    private static Dictionary<string, object?> Member(
        string  value,
        string? reference = null,
        string? type      = "User",
        string? display   = null
    ) {
        var member = new Dictionary<string, object?> {
            ["value"] = value,
            ["$ref"]  = reference ?? $"{Users}/{value}",
        };
        if (type is not null) {
            member["type"] = type;
        }

        if (display is not null) {
            member["display"] = display;
        }

        return member;
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
