using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Exceptions;
using Schemata.Abstractions.Tenancy;
using Schemata.Common;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository;
using Schemata.Tenancy.Foundation.Features;
using Schemata.Tenancy.Foundation.Services;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;
using Xunit;
using System.IO;
using System.Linq;
using LinqToDB;
using LinqToDB.Data;
using LinqToDB.Mapping;
using Schemata.Entity.LinqToDB;

namespace Schemata.Tenancy.Integration.Tests;

[Trait("Layer", "Integration")]
public class TenantStorageShould
{
    [Theory]
    [InlineData("update")]
    [InlineData("display")]
    [InlineData("localized")]
    public async Task Same_Manager_Commits_Two_Mutations_And_Invalidates_After_Each(string operation) {
        using var database = new SqliteConnection("Data Source=:memory:");
        await database.OpenAsync();
        await using var root = Build(database);
        await using var scope = root.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<Database>().Database.EnsureCreatedAsync();
        var manager = scope.ServiceProvider.GetRequiredService<ITenantManager<SchemataTenant>>();
        var tenant = new SchemataTenant { Name = "repeat", DisplayName = "before" };
        await manager.CreateAsync(tenant, default);
        var factory = root.GetRequiredService<ITenantServiceScopeFactory<SchemataTenant>>();
        var identity = new TenantIdentity(tenant.Uid);
        await using var initial = await factory.CreateAsync(identity);
        var previous = initial.ServiceProvider.GetRequiredService<View>();
        foreach (var value in new[] { "first", "second" }) {
            if (operation == "update") {
                tenant.DisplayName = value;
                await manager.UpdateAsync(tenant, default);
            } else if (operation == "display") await manager.SetDisplayNameAsync(tenant, value, default);
            else await manager.SetDisplayNamesAsync(tenant, new() { ["en"] = value }, default);
            await using var fresh = await factory.CreateAsync(identity);
            var view = fresh.ServiceProvider.GetRequiredService<View>();
            Assert.NotSame(previous, view);
            Assert.Equal(tenant.Timestamp, view.Version);
            await using var reader = root.CreateAsyncScope();
            var row = await reader.ServiceProvider.GetRequiredService<Database>().Set<SchemataTenant>().SingleAsync();
            Assert.Equal(value, operation == "localized" ? row.DisplayNames!["en"] : row.DisplayName);
            Assert.Equal(tenant.Timestamp, row.Timestamp);
            previous = view;
        }
    }

    [Theory]
    [InlineData("update")]
    [InlineData("display")]
    [InlineData("localized")]
    public async Task Same_Linq_Manager_Commits_Twice_And_Publishes_Fresh_Provider(string operation) {
        var path = Path.Combine(Path.GetTempPath(), $"schemata-tenant-repeat-{Guid.NewGuid():N}.db");
        try {
            var mapping = new MappingSchema();
            mapping.AddMetadataReader(new SystemComponentModelDataAnnotationsSchemaAttributeReader());
            var options = new DataOptions().UseSQLite($"Data Source={path};Pooling=False").UseMappingSchema(mapping);
            using (var database = new Connection(options)) {
                database.CreateTable<SchemataTenant>(); database.CreateTable<SchemataTenantHost>();
            }
            var services = new ServiceCollection();
            services.AddSingleton<Func<Connection>>(_ => () => new(options));
            services.AddRepository<SchemataTenant, LinqToDbRepository<Connection, SchemataTenant>>();
            services.AddRepository<SchemataTenantHost, LinqToDbRepository<Connection, SchemataTenantHost>>();
            new SchemataTenancyFeature<SchemataTenantManager<SchemataTenant>, SchemataTenant>()
                .ConfigureServices(services, new(), new(), new ConfigurationBuilder().Build(), null!);
            services.Configure<SchemataTenancyOptions>(o => o.DynamicOverrides.Add((_, tenant, _) => tenant.AddSingleton<View>()));
            await using var root = services.BuildServiceProvider();
            await using var scope = root.CreateAsyncScope();
            var manager = scope.ServiceProvider.GetRequiredService<ITenantManager<SchemataTenant>>();
            var tenant = new SchemataTenant { Name = "repeat", DisplayName = "before" };
            await manager.CreateAsync(tenant, default);
            var factory = root.GetRequiredService<ITenantServiceScopeFactory<SchemataTenant>>();
            await using var initial = await factory.CreateAsync(new(tenant.Uid));
            var previous = initial.ServiceProvider.GetRequiredService<View>();
            foreach (var value in new[] { "first", "second" }) {
                if (operation == "update") { tenant.DisplayName = value; await manager.UpdateAsync(tenant, default); }
                else if (operation == "display") await manager.SetDisplayNameAsync(tenant, value, default);
                else await manager.SetDisplayNamesAsync(tenant, new() { ["en"] = value }, default);
                await using var fresh = await factory.CreateAsync(new(tenant.Uid));
                var view = fresh.ServiceProvider.GetRequiredService<View>();
                Assert.NotSame(previous, view); Assert.Equal(tenant.Timestamp, view.Version);
                await using var repository = root.GetRequiredService<IRepository<SchemataTenant>>();
                var current = (await repository.SingleOrDefaultAsync(q => q.Where(row => row.Uid == tenant.Uid)))!;
                Assert.Equal(value, operation == "localized" ? current.DisplayNames!["en"] : current.DisplayName);
                Assert.Equal(tenant.Timestamp, current.Timestamp); previous = view;
            }
        } finally { File.Delete(path); }
    }

    public sealed class Connection(DataOptions options) : DataConnection(options);

    [Fact]
    public async Task Host_Mutation_Advances_Version_And_Second_Node_Uses_Fresh_Provider_Without_Disposing_Lease() {
        using var database = new SqliteConnection("Data Source=:memory:");
        await database.OpenAsync();
        await using var first = Build(database);
        await using var second = Build(database);
        await using (var scope = first.CreateAsyncScope()) {
            await scope.ServiceProvider.GetRequiredService<Database>().Database.EnsureCreatedAsync();
        }
        var tenant = new SchemataTenant { Name = "acme", DisplayName = "before" };
        await using (var scope = first.CreateAsyncScope()) {
            await scope.ServiceProvider.GetRequiredService<ITenantManager<SchemataTenant>>().CreateAsync(tenant, default);
        }
        var factory = second.GetRequiredService<ITenantServiceScopeFactory<SchemataTenant>>();
        var identity = new TenantIdentity(tenant.Uid);
        var old = await factory.CreateAsync(identity);
        var oldView = old.ServiceProvider.GetRequiredService<View>();
        var version = tenant.Timestamp;
        await using (var scope = first.CreateAsyncScope()) {
            await scope.ServiceProvider.GetRequiredService<ITenantManager<SchemataTenant>>().SetHostsAsync(tenant, [" ACME.TEST "], default);
        }
        Assert.NotEqual(version, tenant.Timestamp);
        var current = await factory.CreateAsync(identity);
        var currentView = current.ServiceProvider.GetRequiredService<View>();
        Assert.NotSame(oldView, currentView);
        Assert.False(oldView.Disposed);
        await using (var scope = second.CreateAsyncScope()) {
            var manager = scope.ServiceProvider.GetRequiredService<ITenantManager<SchemataTenant>>();
            Assert.Equal(tenant.Uid, (await manager.FindByHost("acme.test", default))!.Uid);
            var row = await scope.ServiceProvider.GetRequiredService<Database>().Set<SchemataTenantHost>().SingleAsync();
            Assert.Equal("tenants/acme", row.Parent);
            Assert.Equal("tenants/acme/hosts/acme.test", row.CanonicalName);
            var predicate = ResourceNameDescriptor.ForType<SchemataTenantHost>().BuildParentPredicate<SchemataTenantHost>(new() { ["tenant"] = "acme" })!;
            Assert.Equal(row.Uid, (await scope.ServiceProvider.GetRequiredService<Database>().Set<SchemataTenantHost>().SingleAsync(predicate)).Uid);
        }
        await using (var scope = first.CreateAsyncScope()) {
            await scope.ServiceProvider.GetRequiredService<ITenantManager<SchemataTenant>>().DeleteAsync(tenant, default);
        }
        await Assert.ThrowsAsync<TenantResolveException>(() => factory.CreateAsync(identity).AsTask());
        Assert.False(oldView.Disposed);
        second.GetRequiredService<ITenantProviderCache>().Remove(tenant.Uid.ToString());
        Assert.False(oldView.Disposed);
        await old.DisposeAsync();
        Assert.True(oldView.Disposed);
        await current.DisposeAsync();
        Assert.True(currentView.Disposed);
    }

    internal static ServiceProvider Build(SqliteConnection connection) {
        var services = new ServiceCollection();
        services.AddDbContextFactory<Database>(options => options.UseSqlite(connection).ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
        services.AddRepository<SchemataTenant, EfCoreRepository<Database, SchemataTenant>>();
        services.AddRepository<SchemataTenantHost, EfCoreRepository<Database, SchemataTenantHost>>();
        new SchemataTenancyFeature<SchemataTenantManager<SchemataTenant>, SchemataTenant>()
            .ConfigureServices(services, new(), new(), new ConfigurationBuilder().Build(), null!);
        services.Configure<SchemataTenancyOptions>(options => options.DynamicOverrides.Add((_, tenant, _) => tenant.AddSingleton<View>()));
        return services.BuildServiceProvider();
    }

    public sealed class Database(DbContextOptions<Database> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder model) {
            model.Entity<SchemataTenant>().Ignore(row => row.Hosts);
            model.Entity<SchemataTenantHost>();
            base.OnModelCreating(model);
        }
    }

    public sealed class View(SchemataTenant tenant) : IDisposable
    {
        public Guid Version => tenant.Timestamp;
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }
}
