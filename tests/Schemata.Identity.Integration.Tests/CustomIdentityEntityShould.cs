using System;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using LinqToDB;
using LinqToDB.Data;
using LinqToDB.Mapping;
using EfTable = System.ComponentModel.DataAnnotations.Schema.TableAttribute;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Entities;
using Schemata.Core;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.LinqToDB;
using Schemata.Entity.Repository;
using Schemata.Identity.Foundation.Features;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Mutations;
using Schemata.Identity.Skeleton.Stores;
using Xunit;

namespace Schemata.Identity.Integration.Tests;

/// <summary>
///     Proves a host-selected seven-entity Identity installation end to end:
///     <c>UseIdentity&lt;TUser, TRole, TUserClaim, TRoleClaim, TUserLogin, TUserToken, TUserRole&gt;</c>
///     registers the closed stores and mutation owners, and registration, sign-in, claims, roles, and
///     tokens persist only the host entity rows on both repository providers.
/// </summary>
[Trait("Layer", "Integration")]
public class CustomIdentityEntityShould
{
    private const string Password = "Host-Pass123!";

    [CanonicalName("users/{user}")]
    [EfTable("MyUsers")]
    public sealed class MyUser : SchemataUser;

    [CanonicalName("roles/{role}")]
    [EfTable("MyRoles")]
    public sealed class MyRole : SchemataRole;

    [EfTable("MyUserClaims")]
    public sealed class MyUserClaim : SchemataUserClaim;

    [EfTable("MyRoleClaims")]
    public sealed class MyRoleClaim : SchemataRoleClaim;

    [EfTable("MyUserLogins")]
    public sealed class MyUserLogin : SchemataUserLogin;

    [EfTable("MyUserTokens")]
    public sealed class MyUserToken : SchemataUserToken;

    [EfTable("MyUserRoles")]
    public sealed class MyUserRole : SchemataUserRole;

    public sealed class CustomContext(DbContextOptions<CustomContext> options) : DbContext(options)
    {
        public DbSet<MyUser> Users { get; set; } = null!;
        public DbSet<MyRole> Roles { get; set; } = null!;
        public DbSet<MyUserClaim> UserClaims { get; set; } = null!;
        public DbSet<MyRoleClaim> RoleClaims { get; set; } = null!;
        public DbSet<MyUserLogin> UserLogins { get; set; } = null!;
        public DbSet<MyUserToken> UserTokens { get; set; } = null!;
        public DbSet<MyUserRole> UserRoles { get; set; } = null!;
    }

    public sealed class CustomConnection(DataOptions options) : DataConnection(options);

    [Fact]
    public void Forward_The_Default_UseIdentity_To_The_Built_In_Entities() {
        var builder = new SchemataBuilder(new ConfigurationBuilder().Build(), null!);
        builder.UseIdentity();

        Assert.True(builder.HasFeature<SchemataIdentityFeature<SchemataUser, SchemataRole, SchemataUserClaim,
            SchemataRoleClaim, SchemataUserLogin, SchemataUserToken, SchemataUserRole,
            SchemataUserStore<SchemataUser, SchemataRole, SchemataUserClaim, SchemataUserRole, SchemataUserLogin,
                SchemataUserToken, SchemataRoleClaim>,
            SchemataRoleStore<SchemataRole, SchemataRoleClaim, SchemataUserRole>>>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Register_Login_Claim_Role_And_Token_On_Host_Entities(bool linq) {
        await using var host = await Host.CreateAsync(linq);

        Guid uid;
        string canonical;
        using (var scope = host.Services.CreateScope()) {
            var provider = scope.ServiceProvider;
            var users    = provider.GetRequiredService<UserManager<MyUser>>();
            var user     = new MyUser { Name = "host-user", UserName = "host-user", DisplayName = "Host User" };
            var created  = await users.CreateAsync(user, Password);
            Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(e => e.Description)));
            uid       = user.Uid;
            canonical = Assert.IsType<string>(user.CanonicalName);

            var signIn = provider.GetRequiredService<SignInManager<MyUser>>();
            Assert.Equal(SignInResult.Success, await signIn.CheckPasswordSignInAsync(user, Password, false));

            var store = Assert.IsType<SchemataUserStore<MyUser, MyRole, MyUserClaim, MyUserRole, MyUserLogin,
                MyUserToken, MyRoleClaim>>(provider.GetRequiredService<IUserStore<MyUser>>());

            await store.AddClaimsAsync(user, [new Claim("tenant", "acme")]);
            Assert.Contains(await store.GetClaimsAsync(user), c => c.Type == "tenant" && c.Value == "acme");

            var roles = provider.GetRequiredService<IRoleStore<MyRole>>();
            var role  = new MyRole { Name = "my-role", NormalizedName = "MY-ROLE", DisplayName = "my-role" };
            Assert.True((await roles.CreateAsync(role, CancellationToken.None)).Succeeded);
            await store.AddToRoleAsync(user, "MY-ROLE");
            Assert.Contains("my-role", await store.GetRolesAsync(user));
            Assert.True(await store.IsInRoleAsync(user, "MY-ROLE"));

            var roleClaims = Assert.IsAssignableFrom<IRoleClaimStore<MyRole>>(roles);
            await roleClaims.AddClaimAsync(role, new Claim("permission", "read"), CancellationToken.None);

            var tokens = (IUserAuthenticationTokenStore<MyUser>)store;
            await tokens.SetTokenAsync(user, "host", "session", "tok-1", CancellationToken.None);
            Assert.Equal("tok-1", await tokens.GetTokenAsync(user, "host", "session", CancellationToken.None));

            await store.AddLoginAsync(user, new UserLoginInfo("github", "host-login", "GitHub"));
            Assert.Contains(await store.GetLoginsAsync(user),
                login => login.LoginProvider == "github" && login.ProviderKey == "host-login");
            Assert.Equal(uid, (await store.FindByLoginAsync("github", "host-login"))?.Uid);

            Assert.Null(provider.GetService<IUserStore<SchemataUser>>());
            Assert.IsType<UserResourceMutation<MyUser, MyUserClaim, MyUserRole, MyUserLogin, MyUserToken>>(
                provider.GetRequiredService<IResourceMutation<MyUser>>());
            Assert.IsType<RoleResourceMutation<MyRole, MyRoleClaim, MyUserRole>>(
                provider.GetRequiredService<IResourceMutation<MyRole>>());
        }

        using var verify = host.Services.CreateScope();
        var repos = verify.ServiceProvider;

        var userRow = await repos.GetRequiredService<IRepository<MyUser>>()
                                 .SingleOrDefaultAsync(q => q.Where(u => u.Uid == uid));
        Assert.IsType<MyUser>(userRow);
        Assert.Equal("users/host-user", canonical);

        var roleRow = await repos.GetRequiredService<IRepository<MyRole>>()
                                 .SingleOrDefaultAsync(q => q.Where(r => r.CanonicalName == "roles/my-role"));
        Assert.IsType<MyRole>(roleRow);
        Assert.Equal("my-role", roleRow!.DisplayName);

        var claimRow = await repos.GetRequiredService<IRepository<MyUserClaim>>()
                                  .SingleOrDefaultAsync(q => q.Where(c => c.UserId == canonical));
        Assert.IsType<MyUserClaim>(claimRow);
        Assert.Equal("acme", claimRow!.ClaimValue);

        var roleClaimRow = await repos.GetRequiredService<IRepository<MyRoleClaim>>()
                                      .SingleOrDefaultAsync(q => q.Where(c => c.RoleId == "roles/my-role"));
        Assert.IsType<MyRoleClaim>(roleClaimRow);
        Assert.Equal("read", roleClaimRow!.ClaimValue);

        var linkRow = await repos.GetRequiredService<IRepository<MyUserRole>>()
                                 .SingleOrDefaultAsync(q => q.Where(l => l.UserId == canonical));
        Assert.IsType<MyUserRole>(linkRow);
        Assert.Equal("roles/my-role", linkRow!.RoleId);

        var tokenRow = await repos.GetRequiredService<IRepository<MyUserToken>>()
                                  .SingleOrDefaultAsync(q => q.Where(t => t.UserId == canonical && t.Name == "session"));
        Assert.IsType<MyUserToken>(tokenRow);
        Assert.Equal("tok-1", tokenRow!.Value);

        var loginRow = await repos.GetRequiredService<IRepository<MyUserLogin>>()
                                  .SingleOrDefaultAsync(q => q.Where(l => l.UserId == canonical && l.LoginProvider == "github"));
        Assert.IsType<MyUserLogin>(loginRow);
        Assert.Equal("host-login", loginRow!.ProviderKey);

        if (linq) {
            Assert.Equal("MyUsers", Assert.IsAssignableFrom<LinqToDbRepository<CustomConnection, MyUser>>(
                repos.GetRequiredService<IRepository<MyUser>>()).TableName);
            Assert.Equal("MyRoles", Assert.IsAssignableFrom<LinqToDbRepository<CustomConnection, MyRole>>(
                repos.GetRequiredService<IRepository<MyRole>>()).TableName);
            Assert.Equal("MyUserClaims", Assert.IsAssignableFrom<LinqToDbRepository<CustomConnection, MyUserClaim>>(
                repos.GetRequiredService<IRepository<MyUserClaim>>()).TableName);
            Assert.Equal("MyRoleClaims", Assert.IsAssignableFrom<LinqToDbRepository<CustomConnection, MyRoleClaim>>(
                repos.GetRequiredService<IRepository<MyRoleClaim>>()).TableName);
            Assert.Equal("MyUserLogins", Assert.IsAssignableFrom<LinqToDbRepository<CustomConnection, MyUserLogin>>(
                repos.GetRequiredService<IRepository<MyUserLogin>>()).TableName);
            Assert.Equal("MyUserTokens", Assert.IsAssignableFrom<LinqToDbRepository<CustomConnection, MyUserToken>>(
                repos.GetRequiredService<IRepository<MyUserToken>>()).TableName);
            Assert.Equal("MyUserRoles", Assert.IsAssignableFrom<LinqToDbRepository<CustomConnection, MyUserRole>>(
                repos.GetRequiredService<IRepository<MyUserRole>>()).TableName);
        } else {
            await using var db = await repos.GetRequiredService<IDbContextFactory<CustomContext>>().CreateDbContextAsync();
            Assert.Equal("MyUsers", db.Model.FindEntityType(typeof(MyUser))!.GetTableName());
            Assert.Equal("MyRoles", db.Model.FindEntityType(typeof(MyRole))!.GetTableName());
            Assert.Equal("MyUserClaims", db.Model.FindEntityType(typeof(MyUserClaim))!.GetTableName());
            Assert.Equal("MyRoleClaims", db.Model.FindEntityType(typeof(MyRoleClaim))!.GetTableName());
            Assert.Equal("MyUserLogins", db.Model.FindEntityType(typeof(MyUserLogin))!.GetTableName());
            Assert.Equal("MyUserTokens", db.Model.FindEntityType(typeof(MyUserToken))!.GetTableName());
            Assert.Equal("MyUserRoles", db.Model.FindEntityType(typeof(MyUserRole))!.GetTableName());
            Assert.Equal(1, await db.Set<MyUserRole>().CountAsync());
            Assert.Equal(1, await db.Set<MyUserLogin>().CountAsync());
        }
    }

    private sealed class Host(ServiceProvider services, string path) : IAsyncDisposable
    {
        public ServiceProvider Services => services;

        public static async Task<Host> CreateAsync(bool linq) {
            var path     = Path.Combine(Path.GetTempPath(), $"schemata-custom-identity-{Guid.NewGuid():N}.db");
            var services = new ServiceCollection();
            if (linq) {
                var mapping = new MappingSchema();
                mapping.AddMetadataReader(new SystemComponentModelDataAnnotationsSchemaAttributeReader());
                var options = new DataOptions().UseSQLite($"Data Source={path};Pooling=False").UseMappingSchema(mapping);
                services.AddSingleton<Func<CustomConnection>>(_ => () => new(options));
                using var db = new CustomConnection(options);
                db.CreateTable<MyUser>("MyUsers");
                db.CreateTable<MyRole>("MyRoles");
                db.CreateTable<MyUserClaim>("MyUserClaims");
                db.CreateTable<MyRoleClaim>("MyRoleClaims");
                db.CreateTable<MyUserLogin>("MyUserLogins");
                db.CreateTable<MyUserToken>("MyUserTokens");
                db.CreateTable<MyUserRole>("MyUserRoles");
                services.AddRepository<MyUser, LinqToDbRepository<CustomConnection, MyUser>>();
                services.AddRepository<MyRole, LinqToDbRepository<CustomConnection, MyRole>>();
                services.AddRepository<MyUserClaim, LinqToDbRepository<CustomConnection, MyUserClaim>>();
                services.AddRepository<MyRoleClaim, LinqToDbRepository<CustomConnection, MyRoleClaim>>();
                services.AddRepository<MyUserLogin, LinqToDbRepository<CustomConnection, MyUserLogin>>();
                services.AddRepository<MyUserToken, LinqToDbRepository<CustomConnection, MyUserToken>>();
                services.AddRepository<MyUserRole, LinqToDbRepository<CustomConnection, MyUserRole>>();
            } else {
                services.AddDbContextFactory<CustomContext>(options => options.UseSqlite($"Data Source={path};Pooling=False")
                    .ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
                services.AddRepository<MyUser, EfCoreRepository<CustomContext, MyUser>>();
                services.AddRepository<MyRole, EfCoreRepository<CustomContext, MyRole>>();
                services.AddRepository<MyUserClaim, EfCoreRepository<CustomContext, MyUserClaim>>();
                services.AddRepository<MyRoleClaim, EfCoreRepository<CustomContext, MyRoleClaim>>();
                services.AddRepository<MyUserLogin, EfCoreRepository<CustomContext, MyUserLogin>>();
                services.AddRepository<MyUserToken, EfCoreRepository<CustomContext, MyUserToken>>();
                services.AddRepository<MyUserRole, EfCoreRepository<CustomContext, MyUserRole>>();
            }

            services.AddHttpContextAccessor();
            var builder = new SchemataBuilder(new ConfigurationBuilder().Build(), null!);
            builder.UseIdentity<MyUser, MyRole, MyUserClaim, MyRoleClaim, MyUserLogin, MyUserToken, MyUserRole>();
            builder.Invoke(services);

            var root = services.BuildServiceProvider();
            if (!linq) {
                await using var db = await root.GetRequiredService<IDbContextFactory<CustomContext>>().CreateDbContextAsync();
                await db.Database.EnsureCreatedAsync();
            }

            return new(root, path);
        }

        public async ValueTask DisposeAsync() {
            await services.DisposeAsync();
            File.Delete(path);
        }
    }
}
