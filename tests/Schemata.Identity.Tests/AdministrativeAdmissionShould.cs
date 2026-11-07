using System;
using System.IO;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Resource;
using Schemata.Core.Building;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository;
using Schemata.Identity.Foundation;
using Schemata.Identity.Foundation.Services;
using Schemata.Identity.Skeleton;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Services;
using Schemata.Identity.Skeleton.Managers;
using Schemata.Identity.Skeleton.Stores;
using Schemata.Identity.Tests.Fixtures;
using Schemata.Resource.Foundation;
using Xunit;

namespace Schemata.Identity.Tests;

[Trait("Layer", "Unit")]
public class AdministrativeAdmissionShould
{
    [Fact]
    public async Task Inactive_User_Fails_Password_Before_Credential_Creation() {
        var store = new Mock<IUserStore<SchemataUser>>();
        var options = Options.Create(new IdentityOptions());
        var users = new Mock<SchemataUserManager<SchemataUser>>(MockBehavior.Loose,
            Mock.Of<IServiceProvider>(), store.Object, options, new PasswordHasher<SchemataUser>(),
            Array.Empty<IUserValidator<SchemataUser>>(), Array.Empty<IPasswordValidator<SchemataUser>>(),
            new UpperInvariantLookupNormalizer(), new IdentityErrorDescriber(),
            NullLogger<SchemataUserManager<SchemataUser>>.Instance);
        using var manager = users.Object;
        var user = new SchemataUser { UserName = "inactive", AdministrativelyActive = false };
        users.Setup(value => value.IsAdministrativelyActiveAsync(user, It.IsAny<CancellationToken>()))
             .ReturnsAsync(false);
        users.SetupGet(value => value.SupportsUserLockout).Returns(true);
        var claims = new Mock<IUserClaimsPrincipalFactory<SchemataUser>>(MockBehavior.Strict);
        var authentication = new Mock<IAuthenticationService>(MockBehavior.Strict);
        using var services = new ServiceCollection().AddSingleton(authentication.Object).BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        var sign = new SchemataSignInManager<SchemataUser>(manager,
            new HttpContextAccessor { HttpContext = context }, claims.Object, options,
            NullLogger<SignInManager<SchemataUser>>.Instance, Mock.Of<IAuthenticationSchemeProvider>(),
            Mock.Of<IUserConfirmation<SchemataUser>>(),
            new ConfiguredUserAliasResolver(UserNameAliasMode.Disabled, new E164PhoneNumberNormalizer()), []);

        var result = await sign.CheckPasswordSignInAsync(user, "correct-password", false);

        Assert.True(result.IsNotAllowed);
        users.Verify(manager => manager.CheckPasswordAsync(It.IsAny<SchemataUser>(), It.IsAny<string>()), Times.Never);
        claims.Verify(factory => factory.CreateAsync(It.IsAny<SchemataUser>()), Times.Never);
        authentication.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Inactive_User_Fails_Ticket_Renewal_Before_Principal_Rebuild() {
        var now = new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.Zero);
        var clock = new Mock<TimeProvider>();
        clock.Setup(provider => provider.GetUtcNow()).Returns(now);
        using var host = new IdentityHandlerTestHost(services => services.AddSingleton(clock.Object));
        var user = new SchemataUser { AdministrativelyActive = false };
        var original = new ClaimsPrincipal(new ClaimsIdentity("refresh"));
        host.SignIn.Setup(manager => manager.ValidateSecurityStampAsync(original)).ReturnsAsync(user);
        host.Users.Setup(manager => manager.IsAdministrativelyActiveAsync(user, It.IsAny<CancellationToken>()))
             .ReturnsAsync(false);
        var ticket = new AuthenticationTicket(original, new AuthenticationProperties {
            ExpiresUtc = now.AddMinutes(5),
        }, "refresh");

        var result = await host.Handler.RefreshAsync(ticket, new ClaimsPrincipal(), CancellationToken.None);

        Assert.Equal(IdentityStatus.Challenge, result.Status);
        Assert.Null(result.Data);
        host.SignIn.Verify(manager => manager.CreateUserPrincipalAsync(It.IsAny<SchemataUser>()), Times.Never);
    }
}

[Trait("Layer", "Integration")]
public sealed class AdministrativeAdmissionPersistenceShould
{
    [Fact]
    public async Task Reactivation_Persists_Lockout_And_Rotates_Only_Changed_State() {
        await using var host = await AdmissionHost.CreateAsync();
        var user = await host.CreateUserAsync();
        using var scope = host.Services.CreateScope();
        var users = AdmissionHost.Users(scope.ServiceProvider);
        Assert.True(await users.IsAdministrativelyActiveAsync(user));
        var lockoutEnd = DateTimeOffset.UtcNow.AddHours(1);
        user.LockoutEnd = lockoutEnd;
        Assert.True((await users.UpdateAsync(user)).Succeeded);
        var originalStamp = user.SecurityStamp;

        Assert.True((await users.SetAdministrativeActiveAsync(user, false)).Succeeded);
        var inactive = await host.FreshUserAsync(user.Uid);
        Assert.False(inactive.AdministrativelyActive);
        Assert.Equal(lockoutEnd, inactive.LockoutEnd);
        Assert.NotEqual(originalStamp, inactive.SecurityStamp);
        var inactiveStamp = inactive.SecurityStamp;

        Assert.True((await users.SetAdministrativeActiveAsync(user, true)).Succeeded);
        var reactivated = await host.FreshUserAsync(user.Uid);
        Assert.True(reactivated.AdministrativelyActive);
        Assert.Equal(lockoutEnd, reactivated.LockoutEnd);
        Assert.NotEqual(inactiveStamp, reactivated.SecurityStamp);
        Assert.True(await users.IsLockedOutAsync(reactivated));
        var activeStamp = reactivated.SecurityStamp;

        Assert.True((await users.SetAdministrativeActiveAsync(user, true)).Succeeded);
        var unchanged = await host.FreshUserAsync(user.Uid);
        Assert.Equal(activeStamp, unchanged.SecurityStamp);
        Assert.Equal(lockoutEnd, unchanged.LockoutEnd);
    }

    private sealed class AdmissionContext(DbContextOptions<AdmissionContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder model) { model.Entity<SchemataUser>(); }
    }

    private sealed class AdmissionHost(ServiceProvider services, string path) : IAsyncDisposable
    {
        public ServiceProvider Services => services;

        public static async Task<AdmissionHost> CreateAsync() {
            var path = Path.Combine(Path.GetTempPath(), $"schemata-administrative-admission-{Guid.NewGuid():N}.db");
            var collection = new ServiceCollection();
            collection.AddSingleton<ILookupNormalizer>(new UpperInvariantLookupNormalizer());
            collection.AddDbContextFactory<AdmissionContext>(options => options
                .UseSqlite($"Data Source={path};Pooling=False")
                .ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
            collection.AddRepository<SchemataUser, EfCoreRepository<AdmissionContext, SchemataUser>>();
            var registry = new ResourceRegistry();
            registry.Add(new(typeof(SchemataUser)), []);
            collection.TryAddSingleton(registry);
            collection.TryAddSingleton<IResourceTypeResolver, DefaultResourceTypeResolver>();
            collection.TryAddScoped<IResourceMutation<SchemataUser>, ResourceMutation<SchemataUser>>();
            collection.AddSingleton(Mock.Of<IRepository<SchemataUserClaim>>());
            collection.AddSingleton(Mock.Of<IRepository<SchemataUserRole>>());
            collection.AddSingleton(Mock.Of<IRepository<SchemataUserLogin>>());
            collection.AddSingleton(Mock.Of<IRepository<SchemataUserToken>>());
            collection.AddScoped<SchemataUserStore<SchemataUser, SchemataRole, SchemataUserClaim, SchemataUserRole,
                SchemataUserLogin, SchemataUserToken, SchemataRoleClaim>>();
            collection.AddScoped<IUserStore<SchemataUser>>(provider => provider.GetRequiredService<
                SchemataUserStore<SchemataUser, SchemataRole, SchemataUserClaim, SchemataUserRole,
                    SchemataUserLogin, SchemataUserToken, SchemataRoleClaim>>());
            var root = collection.BuildServiceProvider();
            await using var db = await root.GetRequiredService<IDbContextFactory<AdmissionContext>>().CreateDbContextAsync();
            await db.Database.EnsureCreatedAsync();
            return new(root, path);
        }

        public static SchemataUserManager<SchemataUser> Users(IServiceProvider services) {
            return new(services, services.GetRequiredService<IUserStore<SchemataUser>>(),
                Options.Create(new IdentityOptions()), new PasswordHasher<SchemataUser>(), [], [],
                new UpperInvariantLookupNormalizer(), new IdentityErrorDescriber(),
                NullLogger<SchemataUserManager<SchemataUser>>.Instance);
        }

        public async Task<SchemataUser> CreateUserAsync() {
            await using var scope = Services.CreateAsyncScope();
            var users = Users(scope.ServiceProvider);
            var user = new SchemataUser {
                Uid = Guid.NewGuid(), Name = "inactive-user", UserName = "inactive-user",
                ConcurrencyStamp = Guid.NewGuid().ToString("n"), LockoutEnabled = true,
            };
            Assert.True((await users.CreateAsync(user, "Valid-Pass123!")).Succeeded);
            return user;
        }

        public async Task<SchemataUser> FreshUserAsync(Guid uid) {
            await using var scope = Services.CreateAsyncScope();
            var user = await scope.ServiceProvider.GetRequiredService<IUserStore<SchemataUser>>()
                                  .FindByIdAsync(uid.ToString(), CancellationToken.None);
            return Assert.IsType<SchemataUser>(user);
        }

        public async ValueTask DisposeAsync() {
            await services.DisposeAsync();
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
