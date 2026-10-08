using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using LinqToDB;
using LinqToDB.Data;
using LinqToDB.Mapping;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Advisors;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.LinqToDB;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Identity.Skeleton.Entities;
using Schemata.Identity.Skeleton.Stores;
using Xunit;

namespace Schemata.Identity.Integration.Tests;

[Trait("Layer", "Integration")]
public class ClaimRemovalShould
{
    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(false, true, 0)]
    [InlineData(true, false, 0)]
    [InlineData(true, true, 0)]
    [InlineData(false, false, 1)]
    [InlineData(false, true, 1)]
    [InlineData(true, false, 1)]
    [InlineData(true, true, 1)]
    [InlineData(false, false, 205)]
    [InlineData(false, true, 205)]
    [InlineData(true, false, 205)]
    [InlineData(true, true, 205)]
    public async Task Remove_All_Duplicate_Claims_And_Keep_Unmatched_Rows(bool linq, bool role, int count) {
        await using var database = new Database(linq);
        await database.SeedAsync(role, count);
        using (var scope = database.Services.CreateScope()) {
            await RemoveAsync(scope.ServiceProvider, role, CancellationToken.None);
        }
        Assert.Equal(database.Unmatched, await database.ReadAsync(role));
    }

    [Theory]
    [InlineData(false, false, 1, false)]
    [InlineData(false, true, 1, false)]
    [InlineData(true, false, 1, false)]
    [InlineData(true, true, 1, false)]
    [InlineData(false, false, 103, false)]
    [InlineData(false, true, 103, false)]
    [InlineData(true, false, 103, false)]
    [InlineData(true, true, 103, false)]
    [InlineData(false, false, 1, true)]
    [InlineData(false, true, 1, true)]
    [InlineData(true, false, 1, true)]
    [InlineData(true, true, 1, true)]
    [InlineData(false, false, 103, true)]
    [InlineData(false, true, 103, true)]
    [InlineData(true, false, 103, true)]
    [InlineData(true, true, 103, true)]
    [InlineData(false, false, 206, false)]
    [InlineData(true, false, 206, false)]
    [InlineData(false, false, 206, true)]
    [InlineData(true, false, 206, true)]
    public async Task Roll_Back_Every_Claim_When_Removal_Fails_Or_Is_Cancelled(bool linq, bool role, int stop, bool cancel) {
        await using var database = new Database(linq);
        await database.SeedAsync(role, 205);
        var before = await database.ReadAsync(role);
        using var cancellation = new CancellationTokenSource();
        database.Fault.Stop = stop;
        database.Fault.Cancellation = cancel ? cancellation : null;
        using (var scope = database.Services.CreateScope()) {
            if (cancel) {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RemoveAsync(scope.ServiceProvider, role, cancellation.Token));
            } else {
                var error = await Assert.ThrowsAsync<InvalidOperationException>(() => RemoveAsync(scope.ServiceProvider, role, cancellation.Token));
                Assert.Equal("claim removal fault", error.Message);
            }
        }
        Assert.Equal(before, await database.ReadAsync(role));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Keep_The_Same_Store_Usable_After_Consecutive_Removals(bool linq, bool role) {
        await using var database = new Database(linq);
        await database.SeedAsync(role, 205);
        using (var scope = database.Services.CreateScope()) {
            await RemoveAsync(scope.ServiceProvider, role, CancellationToken.None);
            await RemoveAsync(scope.ServiceProvider, role, CancellationToken.None);
            if (role) {
                var store = scope.ServiceProvider.GetRequiredService<SchemataRoleStore<SchemataRole>>();
                var claims = await store.GetClaimsAsync(new() { CanonicalName = "roles/owner" });
                Assert.DoesNotContain(claims, claim => claim.Type == "permission" && claim.Value == "remove");
                Assert.Contains(claims, claim => claim.Type == "permission" && claim.Value == "keep");
            } else {
                var store = scope.ServiceProvider.GetRequiredService<SchemataUserStore<SchemataUser>>();
                var claims = await store.GetClaimsAsync(new() { CanonicalName = "users/owner" });
                Assert.DoesNotContain(claims, claim => claim.Type == "permission" && claim.Value is "remove" or "second");
                Assert.Contains(claims, claim => claim.Type == "permission" && claim.Value == "keep");
            }
        }
        Assert.Equal(database.Unmatched, await database.ReadAsync(role));
    }

    private static Task RemoveAsync(IServiceProvider services, bool role, CancellationToken ct) {
        if (role) {
            var store = services.GetRequiredService<SchemataRoleStore<SchemataRole>>();
            return store.RemoveClaimAsync(new() { CanonicalName = "roles/owner" }, new("permission", "remove"), ct);
        }
        var users = services.GetRequiredService<SchemataUserStore<SchemataUser>>();
        return users.RemoveClaimsAsync(new() { CanonicalName = "users/owner" }, [new Claim("permission", "remove"), new Claim("permission", "second")], ct);
    }

    private sealed class Fault : IRepositoryRemoveAdvisor<SchemataUserClaim>, IRepositoryRemoveAdvisor<SchemataRoleClaim>
    {
        private int _calls;
        public int Order => 0;
        public int Stop { get; set; }
        public CancellationTokenSource? Cancellation { get; set; }
        public Task<AdviseResult> AdviseAsync(AdviceContext context, IRepository<SchemataUserClaim> repository, SchemataUserClaim row, CancellationToken ct) => Advise(ct);
        public Task<AdviseResult> AdviseAsync(AdviceContext context, IRepository<SchemataRoleClaim> repository, SchemataRoleClaim row, CancellationToken ct) => Advise(ct);
        private Task<AdviseResult> Advise(CancellationToken ct) {
            if (++_calls == Stop) {
                if (Cancellation is null) throw new InvalidOperationException("claim removal fault");
                Cancellation.Cancel();
                ct.ThrowIfCancellationRequested();
            }
            return Task.FromResult(AdviseResult.Continue);
        }
    }

    private sealed class Database : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        public ServiceProvider Services { get; }
        public Fault Fault { get; } = new();
        public Guid[] Unmatched { get; private set; } = [];

        public Database(bool linq) {
            var connection = $"Data Source=claims-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
            _connection = new(connection);
            _connection.Open();
            var services = new ServiceCollection();
            if (linq) {
                var mapping = new MappingSchema();
                mapping.AddMetadataReader(new SystemComponentModelDataAnnotationsSchemaAttributeReader());
                var options = new DataOptions().UseSQLite(connection).UseMappingSchema(mapping);
                services.AddSingleton<Func<ClaimConnection>>(_ => () => new(options));
                services.AddRepository<SchemataUser, LinqToDbRepository<ClaimConnection, SchemataUser>>();
                services.AddRepository<SchemataRole, LinqToDbRepository<ClaimConnection, SchemataRole>>();
                services.AddRepository<SchemataUserClaim, LinqToDbRepository<ClaimConnection, SchemataUserClaim>>();
                services.AddRepository<SchemataRoleClaim, LinqToDbRepository<ClaimConnection, SchemataRoleClaim>>();
                services.AddRepository<SchemataUserRole, LinqToDbRepository<ClaimConnection, SchemataUserRole>>();
                services.AddRepository<SchemataUserLogin, LinqToDbRepository<ClaimConnection, SchemataUserLogin>>();
                services.AddRepository<SchemataUserToken, LinqToDbRepository<ClaimConnection, SchemataUserToken>>();
                using var db = new ClaimConnection(options);
                db.CreateTable<SchemataUserClaim>();
                db.CreateTable<SchemataRoleClaim>();
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
            services.AddSingleton<IRepositoryRemoveAdvisor<SchemataUserClaim>>(Fault);
            services.AddSingleton<IRepositoryRemoveAdvisor<SchemataRoleClaim>>(Fault);
            services.AddScoped<SchemataUserStore<SchemataUser>>();
            services.AddScoped<SchemataRoleStore<SchemataRole>>();
            Services = services.BuildServiceProvider();
            if (!linq) {
                using var db = Services.GetRequiredService<IDbContextFactory<IdentityDbContext>>().CreateDbContext();
                db.Database.EnsureCreated();
            }
        }

        public async Task SeedAsync(bool role, int count) {
            using var scope = Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<IRepository<SchemataUserClaim>>();
            var roles = scope.ServiceProvider.GetRequiredService<IRepository<SchemataRoleClaim>>();
            var retained = new System.Collections.Generic.List<Guid>();
            for (var i = 0; i < count + 4; i++) {
                var owner = i == count + 3 ? "other" : "owner";
                var type = i == count + 1 ? "other" : "permission";
                var value = i == count ? "keep" : i == count + 2 ? "second" : "remove";
                if (count == 0 && value == "second") value = "keep-second";
                var uid = Guid.NewGuid();
                if (role) {
                    var row = new SchemataRoleClaim { Uid = uid, RoleId = $"roles/{owner}", ClaimType = type, ClaimValue = value };
                    await roles.AddAsync(row);
                    if (i >= count) retained.Add(row.Uid);
                } else {
                    var row = new SchemataUserClaim { Uid = uid, UserId = $"users/{owner}", ClaimType = type, ClaimValue = value };
                    await users.AddAsync(row);
                    if (i >= count && (i != count + 2 || count == 0)) retained.Add(row.Uid);
                }
            }
            if (role) await roles.CommitAsync(); else await users.CommitAsync();
            Unmatched = retained.OrderBy(id => id).ToArray();
        }

        public async Task<Guid[]> ReadAsync(bool role) {
            using var scope = Services.CreateScope();
            var rows = role
                ? await scope.ServiceProvider.GetRequiredService<IRepository<SchemataRoleClaim>>().ListAsync(q => q.Select(r => r.Uid)).ToListAsync()
                : await scope.ServiceProvider.GetRequiredService<IRepository<SchemataUserClaim>>().ListAsync(q => q.Select(r => r.Uid)).ToListAsync();
            return rows.OrderBy(id => id).ToArray();
        }

        public async ValueTask DisposeAsync() {
            await Services.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class ClaimConnection(DataOptions options) : DataConnection(options);
}
