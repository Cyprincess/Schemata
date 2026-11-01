using System;
using System.Net;
using System.Net.Http;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Identity.Integration.Tests.Fixtures;
using Schemata.Identity.Skeleton.Entities;
using Xunit;

namespace Schemata.Identity.Integration.Tests;

[Trait("Layer", "Integration")]
public class IdentityAssociationsShould
{
    [Fact]
    public async Task Same_User_And_Role_Stores_Read_Current_Detached_Updates() {
        using var factory = new WebAppFactory();
        using var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IUserStore<SchemataUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<IRoleStore<SchemataRole>>();
        var user = new SchemataUser { Name = "coherent-user", UserName = "coherent-user", DisplayName = "before" };
        var role = new SchemataRole { Name = "coherent-role", NormalizedName = "COHERENT-ROLE", DisplayName = "before" };
        Assert.True((await users.CreateAsync(user, default)).Succeeded);
        Assert.True((await roles.CreateAsync(role, default)).Succeeded);
        var oldUser = await users.FindByIdAsync(user.Uid.ToString(), default);
        var oldRole = await roles.FindByIdAsync(role.Uid.ToString(), default);
        SchemataUser detachedUser;
        SchemataRole detachedRole;
        using (var reader = factory.Services.CreateScope()) {
            detachedUser = (await reader.ServiceProvider.GetRequiredService<IUserStore<SchemataUser>>().FindByIdAsync(user.Uid.ToString(), default))!;
            detachedRole = (await reader.ServiceProvider.GetRequiredService<IRoleStore<SchemataRole>>().FindByIdAsync(role.Uid.ToString(), default))!;
        }
        detachedUser.DisplayName = "after";
        detachedRole.DisplayName = "after";
        Assert.True((await users.UpdateAsync(detachedUser, default)).Succeeded);
        Assert.True((await roles.UpdateAsync(detachedRole, default)).Succeeded);
        var currentUser = (await users.FindByIdAsync(user.Uid.ToString(), default))!;
        var currentRole = (await roles.FindByNameAsync("COHERENT-ROLE", default))!;
        Assert.Equal("after", currentUser.DisplayName);
        Assert.Equal("after", currentRole.DisplayName);
        Assert.Equal(detachedUser.Timestamp, currentUser.Timestamp);
        Assert.Equal(detachedRole.Timestamp, currentRole.Timestamp);
        Assert.NotEqual(oldUser!.Timestamp, currentUser.Timestamp);
        Assert.NotEqual(oldRole!.Timestamp, currentRole.Timestamp);
    }

    [Fact]
    public async Task Store_Relationships_By_Canonical_Name_While_Principal_Ids_Remain_Uids() {
        using var factory = new WebAppFactory();
        using var client = factory.CreateClient();
        var user = new SchemataUser { Name = "canonical-user", UserName = "canonical-user" };
        var role = new SchemataRole { Name = "canonical-role", NormalizedName = "CANONICAL-ROLE", DisplayName = "Canonical role" };
        using (var scope = factory.Services.CreateScope()) {
            var users = scope.ServiceProvider.GetRequiredService<IUserStore<SchemataUser>>();
            var roles = scope.ServiceProvider.GetRequiredService<IRoleStore<SchemataRole>>();
            Assert.True((await users.CreateAsync(user, default)).Succeeded);
            Assert.True((await roles.CreateAsync(role, default)).Succeeded);
            await ((IUserClaimStore<SchemataUser>)users).AddClaimsAsync(user, [new Claim("permission", "read")], default);
            await ((IUserLoginStore<SchemataUser>)users).AddLoginAsync(user, new("provider", "external-key", "Provider"), default);
            await ((IUserAuthenticationTokenStore<SchemataUser>)users).SetTokenAsync(user, "provider", "token", "value", default);
            await ((IUserRoleStore<SchemataUser>)users).AddToRoleAsync(user, role.NormalizedName!, default);
            await ((IRoleClaimStore<SchemataRole>)roles).AddClaimAsync(role, new("permission", "write"), default);
            Assert.Equal(user.Uid.ToString(), await users.GetUserIdAsync(user, default));
            Assert.Equal(role.Uid.ToString(), await roles.GetRoleIdAsync(role, default));
        }
        using (var scope = factory.Services.CreateScope()) {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            Assert.Equal(user.CanonicalName, (await db.UserClaims.SingleAsync()).UserId);
            Assert.Equal(user.CanonicalName, (await db.UserLogins.SingleAsync()).UserId);
            Assert.Equal(user.CanonicalName, (await db.UserTokens.SingleAsync()).UserId);
            var link = await db.UserRoles.SingleAsync();
            Assert.Equal(user.CanonicalName, link.UserId);
            Assert.Equal(role.CanonicalName, link.RoleId);
            Assert.Equal(role.CanonicalName, (await db.RoleClaims.SingleAsync()).RoleId);
            var users = scope.ServiceProvider.GetRequiredService<IUserStore<SchemataUser>>();
            Assert.Equal(user.Uid, (await ((IUserLoginStore<SchemataUser>)users).FindByLoginAsync("provider", "external-key", default))!.Uid);
            Assert.Equal(user.Uid, Assert.Single(await ((IUserClaimStore<SchemataUser>)users).GetUsersForClaimAsync(new("permission", "read"), default)).Uid);
            Assert.Equal(user.Uid, Assert.Single(await ((IUserRoleStore<SchemataUser>)users).GetUsersInRoleAsync(role.NormalizedName!, default)).Uid);
            Assert.True(await ((IUserRoleStore<SchemataUser>)users).IsInRoleAsync(user, role.NormalizedName!, default));
            Assert.Equal("value", await ((IUserAuthenticationTokenStore<SchemataUser>)users).GetTokenAsync(user, "provider", "token", default));
        }
        using (var scope = factory.Services.CreateScope()) {
            Assert.True((await scope.ServiceProvider.GetRequiredService<IUserStore<SchemataUser>>().DeleteAsync(user, default)).Succeeded);
        }
        using (var scope = factory.Services.CreateScope()) {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            Assert.Empty(await db.UserClaims.ToListAsync());
            Assert.Empty(await db.UserLogins.ToListAsync());
            Assert.Empty(await db.UserTokens.ToListAsync());
            Assert.Empty(await db.UserRoles.ToListAsync());
            Assert.Single(await db.RoleClaims.ToListAsync());
            Assert.True((await scope.ServiceProvider.GetRequiredService<IRoleStore<SchemataRole>>().DeleteAsync(role, default)).Succeeded);
        }
        using var verify = factory.Services.CreateScope();
        Assert.Empty(await verify.ServiceProvider.GetRequiredService<IdentityDbContext>().RoleClaims.ToListAsync());
    }
    [Fact]
    public async Task Management_Delete_Cleans_Relationships_Before_Name_Reconstruction() {
        using var factory = new WebAppFactory();
        using var client = factory.CreateClient();
        var user = new SchemataUser { Name = "reuse", UserName = "reuse" };
        using (var scope = factory.Services.CreateScope()) {
            var users = scope.ServiceProvider.GetRequiredService<IUserStore<SchemataUser>>();
            Assert.True((await users.CreateAsync(user, default)).Succeeded);
            await ((IUserLoginStore<SchemataUser>)users).AddLoginAsync(user, new("provider", "old-key", "Provider"), default);
            await ((IUserClaimStore<SchemataUser>)users).AddClaimsAsync(user, [new Claim("permission", "old")], default);
            await ((IUserAuthenticationTokenStore<SchemataUser>)users).SetTokenAsync(user, "provider", "token", "old", default);
        }
        using var current = await client.GetAsync("/v1/users/reuse");
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        using var deletion = new HttpRequestMessage(HttpMethod.Delete, "/v1/users/reuse");
        using var deleted = await client.SendAsync(deletion);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        using var fresh = factory.Services.CreateScope();
        var store = fresh.ServiceProvider.GetRequiredService<IUserStore<SchemataUser>>();
        var replacement = new SchemataUser { Name = "reuse", UserName = "reuse" };
        Assert.True((await store.CreateAsync(replacement, default)).Succeeded);
        Assert.NotEqual(user.Uid, replacement.Uid);
        Assert.Equal(user.CanonicalName, replacement.CanonicalName);
        Assert.Null(await ((IUserLoginStore<SchemataUser>)store).FindByLoginAsync("provider", "old-key", default));
        Assert.Empty(await ((IUserClaimStore<SchemataUser>)store).GetClaimsAsync(replacement, default));
        Assert.Null(await ((IUserAuthenticationTokenStore<SchemataUser>)store).GetTokenAsync(replacement, "provider", "token", default));
    }

    [Fact]
    public async Task Role_Management_Delete_Cleans_Claims_And_Memberships_Before_Reconstruction() {
        using var factory = new WebAppFactory();
        using var client = factory.CreateClient();
        var role = new SchemataRole { Name = "reused-role", NormalizedName = "REUSED-ROLE", DisplayName = "Reused role" };
        var user = new SchemataUser { Name = "member", UserName = "member" };
        using (var scope = factory.Services.CreateScope()) {
            var roles = scope.ServiceProvider.GetRequiredService<IRoleStore<SchemataRole>>();
            var users = scope.ServiceProvider.GetRequiredService<IUserStore<SchemataUser>>();
            Assert.True((await roles.CreateAsync(role, default)).Succeeded);
            Assert.True((await users.CreateAsync(user, default)).Succeeded);
            await ((IRoleClaimStore<SchemataRole>)roles).AddClaimAsync(role, new("permission", "old"), default);
            await ((IUserRoleStore<SchemataUser>)users).AddToRoleAsync(user, role.NormalizedName!, default);
        }
        using var deleted = await client.DeleteAsync("/v1/roles/reused-role");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        using var fresh = factory.Services.CreateScope();
        var store = fresh.ServiceProvider.GetRequiredService<IRoleStore<SchemataRole>>();
        var replacement = new SchemataRole { Name = "reused-role", NormalizedName = "REUSED-ROLE" };
        Assert.True((await store.CreateAsync(replacement, default)).Succeeded);
        Assert.NotEqual(role.Uid, replacement.Uid);
        Assert.Equal(role.CanonicalName, replacement.CanonicalName);
        Assert.Empty(await ((IRoleClaimStore<SchemataRole>)store).GetClaimsAsync(replacement, default));
        Assert.False(await ((IUserRoleStore<SchemataUser>)fresh.ServiceProvider.GetRequiredService<IUserStore<SchemataUser>>()).IsInRoleAsync(user, replacement.NormalizedName!, default));
    }

    [Fact]
    public async Task Delete_User_With_A_Stale_Instance_Keeps_User_And_All_Relations() {
        using var factory = new WebAppFactory();
        using var client = factory.CreateClient();
        var user = new SchemataUser { Name = "stale-user", UserName = "stale-user" };
        var role = new SchemataRole { Name = "stale-role", NormalizedName = "STALE-ROLE" };
        using (var scope = factory.Services.CreateScope()) {
            var users = scope.ServiceProvider.GetRequiredService<IUserStore<SchemataUser>>();
            var roles = scope.ServiceProvider.GetRequiredService<IRoleStore<SchemataRole>>();
            Assert.True((await users.CreateAsync(user, default)).Succeeded);
            Assert.True((await roles.CreateAsync(role, default)).Succeeded);
            await ((IUserClaimStore<SchemataUser>)users).AddClaimsAsync(user, [new Claim("permission", "read")], default);
            await ((IUserRoleStore<SchemataUser>)users).AddToRoleAsync(user, role.NormalizedName!, default);
        }
        // A concurrent writer bumps the concurrency stamp, leaving the loaded instance stale.
        using (var scope = factory.Services.CreateScope()) {
            var users = scope.ServiceProvider.GetRequiredService<IUserStore<SchemataUser>>();
            var current = await users.FindByIdAsync(user.Uid.ToString(), default);
            current!.DisplayName = "touched";
            Assert.True((await users.UpdateAsync(current, default)).Succeeded);
        }
        IdentityResult result;
        using (var scope = factory.Services.CreateScope()) {
            result = await scope.ServiceProvider.GetRequiredService<IUserStore<SchemataUser>>().DeleteAsync(user, default);
        }
        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Code == nameof(IdentityErrorDescriber.ConcurrencyFailure));
        using var verify = factory.Services.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.Single(await db.Users.Where(u => u.CanonicalName == user.CanonicalName).ToListAsync());
        Assert.Single(await db.UserClaims.Where(uc => uc.UserId == user.CanonicalName).ToListAsync());
        Assert.Single(await db.UserRoles.Where(ur => ur.UserId == user.CanonicalName).ToListAsync());
    }

    [Fact]
    public async Task Delete_Role_With_A_Stale_Instance_Keeps_Role_Links_And_Claims() {
        using var factory = new WebAppFactory();
        using var client = factory.CreateClient();
        var role = new SchemataRole { Name = "stale-role", NormalizedName = "STALE-ROLE" };
        var user = new SchemataUser { Name = "stale-member", UserName = "stale-member" };
        using (var scope = factory.Services.CreateScope()) {
            var roles = scope.ServiceProvider.GetRequiredService<IRoleStore<SchemataRole>>();
            var users = scope.ServiceProvider.GetRequiredService<IUserStore<SchemataUser>>();
            Assert.True((await roles.CreateAsync(role, default)).Succeeded);
            Assert.True((await users.CreateAsync(user, default)).Succeeded);
            await ((IRoleClaimStore<SchemataRole>)roles).AddClaimAsync(role, new("permission", "read"), default);
            await ((IUserRoleStore<SchemataUser>)users).AddToRoleAsync(user, role.NormalizedName!, default);
        }
        using (var scope = factory.Services.CreateScope()) {
            var roles = scope.ServiceProvider.GetRequiredService<IRoleStore<SchemataRole>>();
            var current = await roles.FindByIdAsync(role.Uid.ToString(), default);
            current!.DisplayName = "touched";
            Assert.True((await roles.UpdateAsync(current, default)).Succeeded);
        }
        IdentityResult result;
        using (var scope = factory.Services.CreateScope()) {
            result = await scope.ServiceProvider.GetRequiredService<IRoleStore<SchemataRole>>().DeleteAsync(role, default);
        }
        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Code == nameof(IdentityErrorDescriber.ConcurrencyFailure));
        using var verify = factory.Services.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.Single(await db.Roles.Where(r => r.CanonicalName == role.CanonicalName).ToListAsync());
        Assert.Single(await db.RoleClaims.Where(rc => rc.RoleId == role.CanonicalName).ToListAsync());
        Assert.Single(await db.UserRoles.Where(ur => ur.RoleId == role.CanonicalName).ToListAsync());
    }

}
