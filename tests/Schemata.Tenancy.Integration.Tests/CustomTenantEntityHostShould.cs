using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LinqToDB;
using LinqToDB.Data;
using LinqToDB.Mapping;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Entities;
using Schemata.Core;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.LinqToDB;
using Schemata.Entity.Repository;
using Schemata.Tenancy.Foundation.Features;
using Schemata.Tenancy.Foundation.Services;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;
using Schemata.Tenancy.Skeleton.Tenancy;
using Xunit;

namespace Schemata.Tenancy.Integration.Tests;

/// <summary>
///     Proves <c>UseTenancy&lt;TTenant, TTenantHost&gt;</c> closes the tenant catalog feature on the
///     selected entities: create, find by identifier and host, update, host replacement, and delete
///     operate only on <see cref="Catalog" /> and <see cref="CatalogHost" /> rows.
/// </summary>
[Trait("Layer", "Integration")]
public class CustomTenantEntityHostShould
{
    [Fact]
    public void Forward_The_Default_UseTenancy_To_The_Built_In_Entities() {
        var builder = new SchemataBuilder(new ConfigurationBuilder().Build(), null!);
        builder.UseTenancy();

        Assert.True(builder.HasFeature<SchemataTenancyFeature<SchemataTenantManager<SchemataTenant, SchemataTenantHost>,
            SchemataTenant, SchemataTenantHost>>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Create_Find_Update_And_Delete_The_Custom_Catalog_And_Hosts(bool linq) {
        await using var host = await Host.CreateAsync(linq);

        Guid uid;
        string canonical;
        using (var scope = host.Services.CreateScope()) {
            var manager = scope.ServiceProvider.GetRequiredService<ITenantManager<Catalog>>();
            var catalog = new Catalog { Name = "acme", DisplayName = "Acme" };
            await manager.CreateAsync(catalog, default);

            uid       = catalog.Uid;
            canonical = Assert.IsType<string>(catalog.CanonicalName);
            Assert.Equal("tenants/acme", canonical);

            await manager.SetHostsAsync(catalog, [" ACME.TEST "], default);
            Assert.Equal("acme.test", Assert.Single(await manager.GetHostsAsync(catalog, default)));
        }

        using (var scope = host.Services.CreateScope()) {
            var hosts = scope.ServiceProvider.GetRequiredService<IRepository<CatalogHost>>();
            var row = await hosts.SingleOrDefaultAsync(q => q.Where(h => h.Parent == canonical));
            Assert.IsType<CatalogHost>(row);
            Assert.Equal("acme.test", row!.Host);
            Assert.Equal("tenants/acme/hosts/acme.test", row.CanonicalName);
        }

        using (var scope = host.Services.CreateScope()) {
            var manager = scope.ServiceProvider.GetRequiredService<ITenantManager<Catalog>>();
            var byId = await manager.FindByTenantId(uid, default);
            Assert.IsType<Catalog>(byId);
            Assert.Equal(canonical, byId!.CanonicalName);

            var byHost = await manager.FindByHost("ACME.TEST", default);
            Assert.IsType<Catalog>(byHost);
            Assert.Equal(uid, byHost!.Uid);

            byId.DisplayName = "Acme Two";
            await manager.UpdateAsync(byId, default);
            await manager.SetDisplayNameAsync(byId, "Acme Three", default);
        }

        using (var scope = host.Services.CreateScope()) {
            var catalog = await scope.ServiceProvider.GetRequiredService<IRepository<Catalog>>()
                                       .FindAsync(new object[] { uid });
            Assert.IsType<Catalog>(catalog);
            Assert.Equal("Acme Three", catalog!.DisplayName);
        }

        using (var scope = host.Services.CreateScope()) {
            var manager = scope.ServiceProvider.GetRequiredService<ITenantManager<Catalog>>();
            await manager.DeleteAsync((await manager.FindByTenantId(uid, default))!, default);
        }

        using (var scope = host.Services.CreateScope()) {
            var manager = scope.ServiceProvider.GetRequiredService<ITenantManager<Catalog>>();
            Assert.Null(await manager.FindByTenantId(uid, default));
            Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<IRepository<Catalog>>().CountAsync(q => q));
            Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<IRepository<CatalogHost>>().CountAsync(q => q));
        }

        Assert.Null(host.Services.GetService<IRepository<SchemataTenant>>());
        Assert.Null(host.Services.GetService<IRepository<SchemataTenantHost>>());
    }

    [System.ComponentModel.DataAnnotations.Schema.Table("Catalogs")]
    [CanonicalName("tenants/{tenant}")]
    public sealed class Catalog : SchemataTenant, ITenant
    {
        public Guid? TenantUid { get; set; }
    }

    [System.ComponentModel.DataAnnotations.Schema.Table("CatalogHosts")]
    [CanonicalName("tenants/{tenant}/hosts/{host}")]
    public sealed class CatalogHost : SchemataTenantHost;

    public sealed class Database(DbContextOptions<Database> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder model) {
            // The CLR base types are concrete and SchemataTenant.Hosts targets SchemataTenantHost, so
            // EF would otherwise keep the unselected base rows in the hierarchy; map the selected
            // types as roots and drop the base-typed navigation.
            model.Entity<Catalog>(builder => {
                builder.HasBaseType((Type?)null);
                builder.Ignore(row => row.Hosts);
            });
            model.Entity<CatalogHost>().HasBaseType((Type?)null);
        }
    }

    public sealed class Connection(DataOptions options) : DataConnection(options);

    private sealed class Host(ServiceProvider services, string path) : IAsyncDisposable
    {
        public ServiceProvider Services => services;

        public static async Task<Host> CreateAsync(bool linq) {
            var path     = Path.Combine(Path.GetTempPath(), $"schemata-custom-tenant-{Guid.NewGuid():N}.db");
            var services = new ServiceCollection();
            if (linq) {
                var mapping = new MappingSchema();
                mapping.AddMetadataReader(new SystemComponentModelDataAnnotationsSchemaAttributeReader());
                var options = new DataOptions().UseSQLite($"Data Source={path};Pooling=False").UseMappingSchema(mapping);
                services.AddSingleton<Func<Connection>>(_ => () => new(options));
                using var database = new Connection(options);
                database.CreateTable<Catalog>("Catalogs");
                database.CreateTable<CatalogHost>("CatalogHosts");
                services.AddRepository<Catalog, LinqToDbRepository<Connection, Catalog>>();
                services.AddRepository<CatalogHost, LinqToDbRepository<Connection, CatalogHost>>();
            } else {
                services.AddDbContextFactory<Database>(options => options.UseSqlite($"Data Source={path};Pooling=False")
                                                                        .ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
                services.AddRepository<Catalog, EfCoreRepository<Database, Catalog>>();
                services.AddRepository<CatalogHost, EfCoreRepository<Database, CatalogHost>>();
            }

            var builder = new SchemataBuilder(new ConfigurationBuilder().Build(), null!);
            builder.UseTenancy<Catalog, CatalogHost>();
            builder.Invoke(services);

            var root = services.BuildServiceProvider();
            if (!linq) {
                await using var database = await root.GetRequiredService<IDbContextFactory<Database>>().CreateDbContextAsync();
                await database.Database.EnsureCreatedAsync();
            }

            return new(root, path);
        }

        public async ValueTask DisposeAsync() {
            await services.DisposeAsync();
            File.Delete(path);
        }
    }
}
