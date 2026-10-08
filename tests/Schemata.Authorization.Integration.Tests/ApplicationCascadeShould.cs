using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LinqToDB;
using LinqToDB.Data;
using LinqToDB.Mapping;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Abstractions.Entities;
using Schemata.Authorization.Foundation.Advisors;
using Schemata.Authorization.Foundation.Managers;
using Schemata.Authorization.Foundation.Mutations;
using Schemata.Authorization.Skeleton.Entities;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.LinqToDB;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Security.Skeleton.Entities;
using Xunit;
using PrimaryKey = Schemata.Abstractions.Entities.PrimaryKeyAttribute;

namespace Schemata.Authorization.Integration.Tests;

[Trait("Category", "Integration")]
public class ApplicationCascadeShould
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeletePhysicalApplication_RemovesDependentsAndAllowsCanonicalReconstruction(bool linq) {
        await using var host = await Host.CreateAsync(linq);
        using (var scope = host.Services.CreateScope()) {
            var applications = scope.ServiceProvider.GetRequiredService<IRepository<SchemataApplication>>();
            var manager = new SchemataApplicationManager<SchemataApplication, Grant>(scope.ServiceProvider,
                scope.ServiceProvider.GetRequiredService<IResourceMutation<SchemataApplication>>());
            var first = new SchemataApplication { Name = "reusable", ClientId = "reusable" };
            await manager.CreateAsync(first);
            var tokens = scope.ServiceProvider.GetRequiredService<IRepository<SchemataToken>>();
            await tokens.AddAsync(new() { Name = "old-authority", Application = first.CanonicalName });
            await tokens.CommitAsync();
            await manager.DeleteAsync(first);
            var replacement = new SchemataApplication { Name = "reusable", ClientId = "reusable" };
            await manager.CreateAsync(replacement);
            Assert.NotEqual(first.Uid, replacement.Uid);
            Assert.Equal(first.CanonicalName, replacement.CanonicalName);
        }
        using (var scope = host.Services.CreateScope()) {
            Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<IRepository<SchemataApplication>>().CountAsync(q => q));
            Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<IRepository<SchemataToken>>().CountAsync(q => q));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ManagerChildRemovalFailure_RollsBackEarlierRemovals(bool linq) {
        await using var host = await Host.CreateAsync(linq, failRemoval: true);
        using (var scope = host.Services.CreateScope()) {
            var applications = scope.ServiceProvider.GetRequiredService<IRepository<Application>>();
            await applications.AddAsync(new() { Name = "failure" });
            await applications.CommitAsync();
            var tokens = scope.ServiceProvider.GetRequiredService<IRepository<SchemataToken>>();
            await tokens.AddAsync(new() { Name = "failure-token", Application = "applications/failure" });
            await tokens.CommitAsync();
            var securities = scope.ServiceProvider.GetRequiredService<IRepository<SchemataSecurity>>();
            await securities.AddAsync(new() { Name = "failure-security", Parent = "applications/failure" });
            await securities.CommitAsync();
        }
        using (var scope = host.Services.CreateScope()) {
            var applications = scope.ServiceProvider.GetRequiredService<IRepository<Application>>();
            var application = await applications.SingleOrDefaultAsync(q => q.Where(a => a.Name == "failure"));
            var manager = new SchemataApplicationManager<Application, Grant>(scope.ServiceProvider,
                scope.ServiceProvider.GetRequiredService<IResourceMutation<Application>>());
            await Assert.ThrowsAsync<Schemata.Abstractions.Exceptions.AbortedException>(() => manager.DeleteAsync(application!));
        }
        using (var scope = host.Services.CreateScope()) {
            Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<IRepository<Application>>().CountAsync(q => q));
            Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<IRepository<SchemataToken>>().CountAsync(q => q));
            Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<IRepository<SchemataSecurity>>().CountAsync(q => q));
        }
    }

    private sealed class RejectSecurityRemoval : IRepositoryRemoveAdvisor<SchemataSecurity> {
        public int Order => 0;
        public Task<Schemata.Abstractions.Advisors.AdviseResult> AdviseAsync(
            Schemata.Abstractions.Advisors.AdviceContext context, IRepository<SchemataSecurity> repository,
            SchemataSecurity entity, System.Threading.CancellationToken ct) {
            throw Schemata.Common.Errors.SchemataResourceErrors.Aborted<SchemataSecurity>(entity.CanonicalName);
        }
    }

    private sealed class BlockApplicationUpdate : IRepositoryUpdateAdvisor<Application> {
        public int Order => 0;
        public Task<Schemata.Abstractions.Advisors.AdviseResult> AdviseAsync(
            Schemata.Abstractions.Advisors.AdviceContext context, IRepository<Application> repository,
            Application entity, System.Threading.CancellationToken ct) {
            return Task.FromResult(Schemata.Abstractions.Advisors.AdviseResult.Block);
        }
    }

    private sealed class BlockApplicationRemoval : IRepositoryRemoveAdvisor<Application> {
        public int Order => 0;
        public Task<Schemata.Abstractions.Advisors.AdviseResult> AdviseAsync(
            Schemata.Abstractions.Advisors.AdviceContext context, IRepository<Application> repository,
            Application entity, System.Threading.CancellationToken ct) {
            return Task.FromResult(Schemata.Abstractions.Advisors.AdviseResult.Block);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task RemoveCanonicalDependents_WithoutDeletingSharedAuthority(bool linq, bool update) {
        await using var host = await Host.CreateAsync(linq);
        const string target = "applications/target";
        const string other = "applications/other";
        using (var scope = host.Services.CreateScope()) {
            var applications = scope.ServiceProvider.GetRequiredService<IRepository<Application>>();
            await applications.AddAsync(new() { Name = "target", CanonicalName = target });
            await applications.AddAsync(new() { Name = "other", CanonicalName = other });
            await applications.CommitAsync();
            var grants = scope.ServiceProvider.GetRequiredService<IRepository<Grant>>();
            await grants.AddAsync(new() { Name = "target", CanonicalName = "authorizations/target", Application = target, DeleteTime = DateTime.UtcNow });
            await grants.CommitAsync();
            var tokens = scope.ServiceProvider.GetRequiredService<IRepository<SchemataToken>>();
            await tokens.AddRangeAsync([
                new() { Name = "live", Application = target, Status = "valid", Family = "shared" },
                new() { Name = "retired", Application = target, Status = "revoked" },
                new() { Name = "participant", Application = target, Type = "session_participant" },
                new() { Name = "parent", Parent = target },
                new() { Name = "grant", Authorization = "authorizations/target" },
                new() { Name = "other", Application = other, Family = "shared" },
                new() { Name = "family", Family = "shared", Type = "family" },
                new() { Name = "session", Parent = "users/shared", Type = "session" },
            ]);
            await tokens.CommitAsync();
            var securities = scope.ServiceProvider.GetRequiredService<IRepository<SchemataSecurity>>();
            await securities.AddRangeAsync([
                new() { Name = "target", Parent = target },
                new() { Name = "issuer", Parent = "https://issuer.example" },
            ]);
            await securities.CommitAsync();
            var mappings = scope.ServiceProvider.GetRequiredService<IRepository<SchemataSubjectMapping>>();
            await mappings.AddRangeAsync([
                new() { Name = "target", Application = target, Subject = "users/shared", PairwiseSubject = "target" },
                new() { Name = "other", Application = other, Subject = "users/shared", PairwiseSubject = "other" },
            ]);
            await mappings.CommitAsync();
        }
        using (var scope = host.Services.CreateScope()) {
            var applications = scope.ServiceProvider.GetRequiredService<IRepository<Application>>();
            var application = await applications.SingleOrDefaultAsync(q => q.Where(a => a.CanonicalName == target));
            Assert.NotNull(application);
            var manager = new SchemataApplicationManager<Application, Grant>(scope.ServiceProvider,
                scope.ServiceProvider.GetRequiredService<IResourceMutation<Application>>());
            if (update) {
                application.DeleteTime = DateTime.UtcNow;
                await manager.UpdateAsync(application);
            } else {
                await manager.DeleteAsync(application);
            }
        }
        using (var scope = host.Services.CreateScope()) {
            var applications = scope.ServiceProvider.GetRequiredService<IRepository<Application>>();
            using var visible = applications.SuppressQuerySoftDelete();
            Assert.Equal(1, await applications.CountAsync(q => q.Where(a => a.CanonicalName == target && a.DeleteTime != null)));
            Assert.Equal(1, await applications.CountAsync(q => q.Where(a => a.CanonicalName == other)));
            var grants = scope.ServiceProvider.GetRequiredService<IRepository<Grant>>();
            using var deleted = grants.SuppressQuerySoftDelete();
            Assert.Equal(0, await grants.CountAsync(q => q));
            var tokens = scope.ServiceProvider.GetRequiredService<IRepository<SchemataToken>>();
            var remaining = await tokens.ListAsync(q => q.OrderBy(t => t.Name).Select(t => t.Name)).ToListAsync();
            Assert.Equal(new[] { "family", "other", "session" }, remaining);
            var securities = scope.ServiceProvider.GetRequiredService<IRepository<SchemataSecurity>>();
            Assert.Equal("https://issuer.example", (await securities.SingleOrDefaultAsync(q => q))!.Parent);
            var mappings = scope.ServiceProvider.GetRequiredService<IRepository<SchemataSubjectMapping>>();
            Assert.Equal(other, (await mappings.SingleOrDefaultAsync(q => q))!.Application);
        }
    }

    [Trait("Layer", "Integration")]
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task BlockedPrimaryWrite_LeavesApplicationAndDependentsUntouched(bool linq, bool update) {
        await using var host = await Host.CreateAsync(linq, blockApplication: true);
        const string target = "applications/blocked";
        using (var scope = host.Services.CreateScope()) {
            var applications = scope.ServiceProvider.GetRequiredService<IRepository<Application>>();
            await applications.AddAsync(new() { Name = "blocked", CanonicalName = target });
            await applications.CommitAsync();
            var grants = scope.ServiceProvider.GetRequiredService<IRepository<Grant>>();
            await grants.AddAsync(new() { Name = "blocked", CanonicalName = "authorizations/blocked", Application = target });
            await grants.CommitAsync();
            var tokens = scope.ServiceProvider.GetRequiredService<IRepository<SchemataToken>>();
            await tokens.AddAsync(new() { Name = "blocked", Application = target });
            await tokens.CommitAsync();
            var securities = scope.ServiceProvider.GetRequiredService<IRepository<SchemataSecurity>>();
            await securities.AddAsync(new() { Name = "blocked", Parent = target });
            await securities.CommitAsync();
            var mappings = scope.ServiceProvider.GetRequiredService<IRepository<SchemataSubjectMapping>>();
            await mappings.AddAsync(new() { Name = "blocked", Application = target, Subject = "users/shared", PairwiseSubject = "blocked" });
            await mappings.CommitAsync();
        }
        using (var scope = host.Services.CreateScope()) {
            var applications = scope.ServiceProvider.GetRequiredService<IRepository<Application>>();
            var application = await applications.SingleOrDefaultAsync(q => q.Where(a => a.CanonicalName == target));
            Assert.NotNull(application);
            var mutation = scope.ServiceProvider.GetRequiredService<IResourceMutation<Application>>();
            MutationResult result;
            if (update) {
                application.DeleteTime = DateTime.UtcNow;
                result = await mutation.UpdateAsync(application);
            } else {
                result = await mutation.DeleteAsync(application);
            }
            Assert.Equal(MutationResult.NoWrite, result);
        }
        using (var scope = host.Services.CreateScope()) {
            var applications = scope.ServiceProvider.GetRequiredService<IRepository<Application>>();
            var application = await applications.SingleOrDefaultAsync(q => q.Where(a => a.CanonicalName == target));
            Assert.NotNull(application);
            Assert.Null(application.DeleteTime);
            var grants = scope.ServiceProvider.GetRequiredService<IRepository<Grant>>();
            Assert.Equal(target, (await grants.SingleOrDefaultAsync(q => q))!.Application);
            var tokens = scope.ServiceProvider.GetRequiredService<IRepository<SchemataToken>>();
            Assert.Equal(target, (await tokens.SingleOrDefaultAsync(q => q))!.Application);
            var securities = scope.ServiceProvider.GetRequiredService<IRepository<SchemataSecurity>>();
            Assert.Equal(target, (await securities.SingleOrDefaultAsync(q => q))!.Parent);
            var mappings = scope.ServiceProvider.GetRequiredService<IRepository<SchemataSubjectMapping>>();
            Assert.Equal(target, (await mappings.SingleOrDefaultAsync(q => q))!.Application);
        }
    }

    [System.ComponentModel.DataAnnotations.Schema.Table("Applications")]
    [CanonicalName("applications/{application}")]
    [PrimaryKey(nameof(Uid))]
    public class Application : SchemataApplication, ISoftDelete {
        public DateTime? DeleteTime { get; set; }
        public DateTime? PurgeTime { get; set; }
    }

    [System.ComponentModel.DataAnnotations.Schema.Table("Grants")]
    [CanonicalName("authorizations/{authorization}")]
    [PrimaryKey(nameof(Uid))]
    public class Grant : SchemataAuthorization, ISoftDelete {
        public DateTime? DeleteTime { get; set; }
        public DateTime? PurgeTime { get; set; }
    }

    public sealed class Context(DbContextOptions<Context> options) : DbContext(options) {
        protected override void OnModelCreating(ModelBuilder model) {
            model.Entity<Application>().HasBaseType((Type?)null);
            model.Entity<SchemataApplication>();
            model.Entity<Grant>();
            model.Entity<SchemataToken>();
            model.Entity<SchemataSecurity>();
            model.Entity<SchemataSubjectMapping>();
        }
    }

    public sealed class Connection(DataOptions options) : DataConnection(options);

    private sealed class Host(ServiceProvider services, string path) : IAsyncDisposable {
        public ServiceProvider Services => services;

        internal static async Task<Host> CreateAsync(bool linq, bool failRemoval = false, bool blockApplication = false) {
            var path = Path.Combine(Path.GetTempPath(), $"schemata-cascade-{Guid.NewGuid():N}.db");
            var services = new ServiceCollection();
            if (linq) {
                var schema = new MappingSchema();
                schema.AddMetadataReader(new SystemComponentModelDataAnnotationsSchemaAttributeReader());
                var options = new DataOptions().UseSQLite($"Data Source={path};Pooling=False").UseMappingSchema(schema);
                services.AddSingleton<Func<Connection>>(_ => () => new(options));
                using var db = new Connection(options);
                db.CreateTable<Application>();
                db.CreateTable<SchemataApplication>();
                db.CreateTable<Grant>();
                db.CreateTable<SchemataToken>();
                db.CreateTable<SchemataSecurity>();
                db.CreateTable<SchemataSubjectMapping>();
            } else {
                services.AddDbContextFactory<Context>(o => o.UseSqlite($"Data Source={path};Pooling=False")
                    .ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
            }
            Register<Application>(services, linq);
            Register<SchemataApplication>(services, linq);
            Register<Grant>(services, linq);
            Register<SchemataToken>(services, linq);
            Register<SchemataSecurity>(services, linq);
            Register<SchemataSubjectMapping>(services, linq);
            // Mirror the production closed-owner registration; the open-generic default has no cascade.
            services.TryAddScoped<IResourceMutation<Application>, ApplicationResourceMutation<Application, Grant>>();
            services.TryAddScoped<IResourceMutation<SchemataApplication>, ApplicationResourceMutation<SchemataApplication, Grant>>();
            if (failRemoval) {
                services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryRemoveAdvisor<SchemataSecurity>, RejectSecurityRemoval>());
            }
            if (blockApplication) {
                services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryUpdateAdvisor<Application>, BlockApplicationUpdate>());
                services.TryAddEnumerable(ServiceDescriptor.Scoped<IRepositoryRemoveAdvisor<Application>, BlockApplicationRemoval>());
            }
            var root = services.BuildServiceProvider();
            if (!linq) {
                await using var db = await root.GetRequiredService<IDbContextFactory<Context>>().CreateDbContextAsync();
                await db.Database.EnsureCreatedAsync();
            }
            return new(root, path);
        }

        private static void Register<T>(IServiceCollection services, bool linq) where T : class {
            if (linq) services.AddRepository<T, LinqToDbRepository<Connection, T>>();
            else services.AddRepository<T, EfCoreRepository<Context, T>>();
        }

        public async ValueTask DisposeAsync() {
            await services.DisposeAsync();
            File.Delete(path);
        }
    }
}
