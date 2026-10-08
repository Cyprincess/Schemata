using System;
using System.Linq;
using System.Threading.Tasks;
using LinqToDB;
using LinqToDB.Data;
using LinqToDB.Mapping;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.LinqToDB;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Mutations;
using Schemata.Identity.Skeleton.Stores;
using Xunit;

namespace Schemata.Identity.Integration.Tests;

[Trait("Layer", "Integration")]
public class DependentCleanupShould
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Leave_Every_Row_Untouched_When_The_Primary_Removal_Is_Blocked(bool linq, bool role) {
        await using var database = new Database(linq, blockPrimary: true);
        await database.SeedAsync();
        using (var scope = database.Services.CreateScope()) {
            MutationResult result;
            if (role) {
                var roles = scope.ServiceProvider.GetRequiredService<IRepository<SchemataRole>>();
                var entity = await roles.SingleOrDefaultAsync(q => q.Where(r => r.CanonicalName == "roles/owner"));
                Assert.NotNull(entity);
                result = await scope.ServiceProvider.GetRequiredService<IResourceMutation<SchemataRole>>().DeleteAsync(entity);
            } else {
                var users = scope.ServiceProvider.GetRequiredService<IRepository<SchemataUser>>();
                var entity = await users.SingleOrDefaultAsync(q => q.Where(u => u.CanonicalName == "users/owner"));
                Assert.NotNull(entity);
                result = await scope.ServiceProvider.GetRequiredService<IResourceMutation<SchemataUser>>().DeleteAsync(entity);
            }
            Assert.Equal(MutationResult.NoWrite, result);
        }
        Assert.Equal(new[] { 2, 2, 2, 2, 2, 2, 2 }, await database.CountsAsync());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Remove_The_Dependents_With_An_Applied_Store_Delete(bool linq, bool role) {
        await using var database = new Database(linq);
        await database.SeedAsync();
        using (var scope = database.Services.CreateScope()) {
            if (role) {
                var roles = scope.ServiceProvider.GetRequiredService<IRepository<SchemataRole>>();
                var entity = await roles.SingleOrDefaultAsync(q => q.Where(r => r.CanonicalName == "roles/owner"));
                Assert.NotNull(entity);
                var store = scope.ServiceProvider.GetRequiredService<SchemataRoleStore<SchemataRole>>();
                Assert.True((await store.DeleteAsync(entity)).Succeeded);
            } else {
                var users = scope.ServiceProvider.GetRequiredService<IRepository<SchemataUser>>();
                var entity = await users.SingleOrDefaultAsync(q => q.Where(u => u.CanonicalName == "users/owner"));
                Assert.NotNull(entity);
                var store = scope.ServiceProvider.GetRequiredService<SchemataUserStore<SchemataUser>>();
                Assert.True((await store.DeleteAsync(entity)).Succeeded);
            }
        }
        int[] expected = role ? [2, 1, 1, 2, 1, 2, 2] : [1, 2, 1, 1, 2, 1, 1];
        Assert.Equal(expected, await database.CountsAsync());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Roll_Back_The_Primary_And_Cleanup_When_A_Dependent_Removal_Is_Blocked(bool linq, bool role) {
        await using var database = new Database(linq, blockClaim: true);
        await database.SeedAsync();
        using (var scope = database.Services.CreateScope()) {
            if (role) {
                var roles = scope.ServiceProvider.GetRequiredService<IRepository<SchemataRole>>();
                var entity = await roles.SingleOrDefaultAsync(q => q.Where(r => r.CanonicalName == "roles/owner"));
                Assert.NotNull(entity);
                var mutation = scope.ServiceProvider.GetRequiredService<IResourceMutation<SchemataRole>>();
                await Assert.ThrowsAsync<FailedPreconditionException>(() => mutation.DeleteAsync(entity));
            } else {
                var users = scope.ServiceProvider.GetRequiredService<IRepository<SchemataUser>>();
                var entity = await users.SingleOrDefaultAsync(q => q.Where(u => u.CanonicalName == "users/owner"));
                Assert.NotNull(entity);
                var mutation = scope.ServiceProvider.GetRequiredService<IResourceMutation<SchemataUser>>();
                await Assert.ThrowsAsync<FailedPreconditionException>(() => mutation.DeleteAsync(entity));
            }
        }
        Assert.Equal(new[] { 2, 2, 2, 2, 2, 2, 2 }, await database.CountsAsync());
    }

    private sealed class BlockPrimary : IRepositoryRemoveAdvisor<SchemataUser>, IRepositoryRemoveAdvisor<SchemataRole>
    {
        public int Order => 0;
        public Task<AdviseResult> AdviseAsync(AdviceContext context, IRepository<SchemataUser> repository, SchemataUser row, System.Threading.CancellationToken ct) {
            return Task.FromResult(AdviseResult.Block);
        }
        public Task<AdviseResult> AdviseAsync(AdviceContext context, IRepository<SchemataRole> repository, SchemataRole row, System.Threading.CancellationToken ct) {
            return Task.FromResult(AdviseResult.Block);
        }
    }

    private sealed class BlockClaim : IRepositoryRemoveAdvisor<SchemataUserClaim>, IRepositoryRemoveAdvisor<SchemataRoleClaim>
    {
        public int Order => 0;
        public Task<AdviseResult> AdviseAsync(AdviceContext context, IRepository<SchemataUserClaim> repository, SchemataUserClaim row, System.Threading.CancellationToken ct) {
            return Task.FromResult(AdviseResult.Block);
        }
        public Task<AdviseResult> AdviseAsync(AdviceContext context, IRepository<SchemataRoleClaim> repository, SchemataRoleClaim row, System.Threading.CancellationToken ct) {
            return Task.FromResult(AdviseResult.Block);
        }
    }

    private sealed class Database : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        public ServiceProvider Services { get; }

        public Database(bool linq, bool blockPrimary = false, bool blockClaim = false) {
            var connection = $"Data Source=deletion-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
            _connection = new(connection);
            _connection.Open();
            var services = new ServiceCollection();
            if (linq) {
                var mapping = new MappingSchema();
                mapping.AddMetadataReader(new SystemComponentModelDataAnnotationsSchemaAttributeReader());
                var options = new DataOptions().UseSQLite(connection).UseMappingSchema(mapping);
                services.AddSingleton<Func<DeletionConnection>>(_ => () => new(options));
                services.AddRepository<SchemataUser, LinqToDbRepository<DeletionConnection, SchemataUser>>();
                services.AddRepository<SchemataRole, LinqToDbRepository<DeletionConnection, SchemataRole>>();
                services.AddRepository<SchemataUserClaim, LinqToDbRepository<DeletionConnection, SchemataUserClaim>>();
                services.AddRepository<SchemataRoleClaim, LinqToDbRepository<DeletionConnection, SchemataRoleClaim>>();
                services.AddRepository<SchemataUserRole, LinqToDbRepository<DeletionConnection, SchemataUserRole>>();
                services.AddRepository<SchemataUserLogin, LinqToDbRepository<DeletionConnection, SchemataUserLogin>>();
                services.AddRepository<SchemataUserToken, LinqToDbRepository<DeletionConnection, SchemataUserToken>>();
                using var db = new DeletionConnection(options);
                db.CreateTable<SchemataUser>();
                db.CreateTable<SchemataRole>();
                db.CreateTable<SchemataUserClaim>();
                db.CreateTable<SchemataRoleClaim>();
                db.CreateTable<SchemataUserRole>();
                db.CreateTable<SchemataUserLogin>();
                db.CreateTable<SchemataUserToken>();
            } else {
                services.AddDbContextFactory<IdentityDbContext>(options => options.UseSqlite(connection).ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
                services.AddRepository<SchemataUser, EfCoreRepository<IdentityDbContext, SchemataUser>>();
                services.AddRepository<SchemataRole, EfCoreRepository<IdentityDbContext, SchemataRole>>();
                services.AddRepository<SchemataUserClaim, EfCoreRepository<IdentityDbContext, SchemataUserClaim>>();
                services.AddRepository<SchemataRoleClaim, EfCoreRepository<IdentityDbContext, SchemataRoleClaim>>();
                services.AddRepository<SchemataUserRole, EfCoreRepository<IdentityDbContext, SchemataUserRole>>();
                services.AddRepository<SchemataUserLogin, EfCoreRepository<IdentityDbContext, SchemataUserLogin>>();
                services.AddRepository<SchemataUserToken, EfCoreRepository<IdentityDbContext, SchemataUserToken>>();
            }
            services.TryAddScoped<IResourceMutation<SchemataUser>, UserResourceMutation<SchemataUser, SchemataUserClaim, SchemataUserRole, SchemataUserLogin, SchemataUserToken>>();
            services.TryAddScoped<IResourceMutation<SchemataRole>, RoleResourceMutation<SchemataRole, SchemataRoleClaim, SchemataUserRole>>();
            if (blockPrimary) {
                services.AddSingleton<IRepositoryRemoveAdvisor<SchemataUser>, BlockPrimary>();
                services.AddSingleton<IRepositoryRemoveAdvisor<SchemataRole>, BlockPrimary>();
            }
            if (blockClaim) {
                services.AddSingleton<IRepositoryRemoveAdvisor<SchemataUserClaim>, BlockClaim>();
                services.AddSingleton<IRepositoryRemoveAdvisor<SchemataRoleClaim>, BlockClaim>();
            }
            services.AddScoped<SchemataUserStore<SchemataUser>>();
            services.AddScoped<SchemataRoleStore<SchemataRole>>();
            Services = services.BuildServiceProvider();
            if (!linq) {
                using var db = Services.GetRequiredService<IDbContextFactory<IdentityDbContext>>().CreateDbContext();
                db.Database.EnsureCreated();
            }
        }

        public async Task SeedAsync() {
            using var scope = Services.CreateScope();
            var provider = scope.ServiceProvider;
            var users = provider.GetRequiredService<IRepository<SchemataUser>>();
            await users.AddRangeAsync([
                new() { Name = "owner", CanonicalName = "users/owner", UserName = "owner" },
                new() { Name = "other", CanonicalName = "users/other", UserName = "other" },
            ]);
            await users.CommitAsync();
            var roles = provider.GetRequiredService<IRepository<SchemataRole>>();
            await roles.AddRangeAsync([
                new() { Name = "owner", CanonicalName = "roles/owner" },
                new() { Name = "other", CanonicalName = "roles/other" },
            ]);
            await roles.CommitAsync();
            var links = provider.GetRequiredService<IRepository<SchemataUserRole>>();
            await links.AddRangeAsync([
                new() { UserId = "users/owner", RoleId = "roles/owner" },
                new() { UserId = "users/other", RoleId = "roles/other" },
            ]);
            await links.CommitAsync();
            var userClaims = provider.GetRequiredService<IRepository<SchemataUserClaim>>();
            await userClaims.AddRangeAsync([
                new() { Uid = Guid.NewGuid(), UserId = "users/owner", ClaimType = "permission", ClaimValue = "owner" },
                new() { Uid = Guid.NewGuid(), UserId = "users/other", ClaimType = "permission", ClaimValue = "other" },
            ]);
            await userClaims.CommitAsync();
            var roleClaims = provider.GetRequiredService<IRepository<SchemataRoleClaim>>();
            await roleClaims.AddRangeAsync([
                new() { Uid = Guid.NewGuid(), RoleId = "roles/owner", ClaimType = "permission", ClaimValue = "owner" },
                new() { Uid = Guid.NewGuid(), RoleId = "roles/other", ClaimType = "permission", ClaimValue = "other" },
            ]);
            await roleClaims.CommitAsync();
            var logins = provider.GetRequiredService<IRepository<SchemataUserLogin>>();
            await logins.AddRangeAsync([
                new() { LoginProvider = "owner", ProviderKey = "owner", UserId = "users/owner" },
                new() { LoginProvider = "other", ProviderKey = "other", UserId = "users/other" },
            ]);
            await logins.CommitAsync();
            var tokens = provider.GetRequiredService<IRepository<SchemataUserToken>>();
            await tokens.AddRangeAsync([
                new() { UserId = "users/owner", LoginProvider = "owner", Name = "owner" },
                new() { UserId = "users/other", LoginProvider = "other", Name = "other" },
            ]);
            await tokens.CommitAsync();
        }

        public async Task<int[]> CountsAsync() {
            using var scope = Services.CreateScope();
            var provider = scope.ServiceProvider;
            return [
                await provider.GetRequiredService<IRepository<SchemataUser>>().CountAsync(q => q),
                await provider.GetRequiredService<IRepository<SchemataRole>>().CountAsync(q => q),
                await provider.GetRequiredService<IRepository<SchemataUserRole>>().CountAsync(q => q),
                await provider.GetRequiredService<IRepository<SchemataUserClaim>>().CountAsync(q => q),
                await provider.GetRequiredService<IRepository<SchemataRoleClaim>>().CountAsync(q => q),
                await provider.GetRequiredService<IRepository<SchemataUserLogin>>().CountAsync(q => q),
                await provider.GetRequiredService<IRepository<SchemataUserToken>>().CountAsync(q => q),
            ];
        }

        public async ValueTask DisposeAsync() {
            await Services.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class DeletionConnection(DataOptions options) : DataConnection(options);
}
