using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LinqToDB;
using LinqToDB.Data;
using LinqToDB.Mapping;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Entities;
using Schemata.Core;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.LinqToDB;
using Schemata.Entity.Repository;
using Schemata.Insight.Foundation.Catalog;
using Schemata.Insight.Foundation.Features;
using Schemata.Insight.Skeleton.Catalog;
using Schemata.Insight.Skeleton.Entities;
using Xunit;
using TableAttribute = System.ComponentModel.DataAnnotations.Schema.TableAttribute;

namespace Schemata.Insight.Tests;

/// <summary>
///     Proves <c>UseInsight&lt;TInsightSource&gt;</c> closes the feature and database catalog on the
///     host-selected source entity, and that the catalog resolves and lists sources persisted
///     through EF Core and LinqToDB.
/// </summary>
[Trait("Layer", "Integration")]
public sealed class CustomInsightSourceShould
{
    [Fact]
    public void Forward_The_Default_UseInsight_To_The_Built_In_Entity() {
        var builder = new SchemataBuilder(new ConfigurationBuilder().Build(), null!);
        builder.UseInsight();

        Assert.True(builder.HasFeature<SchemataInsightFeature<SchemataInsightSource>>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Resolve_And_List_The_Custom_Source_Through_The_Closed_Catalog(bool linq) {
        await using var host = await Host.CreateAsync(linq);

        using (var scope = host.Services.CreateScope()) {
            var mutation = scope.ServiceProvider.GetRequiredService<IResourceMutation<CustomInsightSource>>();
            await mutation.CreateAsync(new() {
                Name   = "live_buyers",
                Driver = "repository",
                Params = """{"binding":"buyers","limit":9007199254740993}""",
            });
        }

        using (var scope = host.Services.CreateScope()) {
            var catalogs = scope.ServiceProvider.GetServices<IInsightSourceCatalog>().ToList();
            var catalog  = Assert.IsType<DatabaseInsightSourceCatalog<CustomInsightSource>>(catalogs[0]);
            Assert.IsType<InMemoryInsightSourceCatalog>(catalogs[1]);

            var config = await catalog.ResolveAsync("live_buyers", CancellationToken.None);
            Assert.NotNull(config);
            Assert.Equal("repository", config.DriverName);
            Assert.Equal("buyers", config.Params["binding"]);
            Assert.Equal(9007199254740993L, config.Params["limit"]);

            Assert.Null(await catalog.ResolveAsync("missing", CancellationToken.None));
            Assert.Contains("live_buyers", await catalog.ListNamesAsync(CancellationToken.None));
        }

        Assert.Null(host.Services.GetService<IRepository<SchemataInsightSource>>());
    }

    [Table("CustomInsightSources")]
    [CanonicalName("customInsightSources/{custom_insight_source}")]
    [Schemata.Abstractions.Entities.PrimaryKey(nameof(Uid))]
    public sealed class CustomInsightSource : SchemataInsightSource;

    public sealed class Database(DbContextOptions<Database> options) : DbContext(options)
    {
        public DbSet<CustomInsightSource> Sources { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder model) {
            // The CLR base type is concrete; map the selected type as its own root so the unselected
            // built-in source does not join the model.
            model.Entity<CustomInsightSource>().HasBaseType((Type?)null);
        }
    }

    public sealed class Connection(DataOptions options) : DataConnection(options);

    private sealed class Host(ServiceProvider services, string path) : IAsyncDisposable
    {
        public ServiceProvider Services => services;

        public static async Task<Host> CreateAsync(bool linq) {
            var path     = Path.Combine(Path.GetTempPath(), $"schemata-custom-insight-{Guid.NewGuid():N}.db");
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(TimeProvider.System);

            if (linq) {
                var mapping = new MappingSchema();
                mapping.AddMetadataReader(new SystemComponentModelDataAnnotationsSchemaAttributeReader());
                var options = new DataOptions().UseSQLite($"Data Source={path};Pooling=False").UseMappingSchema(mapping);
                services.AddSingleton<Func<Connection>>(_ => () => new(options));
                services.AddRepository<CustomInsightSource, LinqToDbRepository<Connection, CustomInsightSource>>();
                services.AddScoped<IUnitOfWork<Connection>, LinqToDbUnitOfWork<Connection>>();

                var builder = new SchemataBuilder(new ConfigurationBuilder().Build(), null!);
                builder.UseInsight<CustomInsightSource>(insight => insight.UseDatabaseCatalog());
                builder.Invoke(services);

                using var connection = new Connection(options);
                connection.CreateTable<CustomInsightSource>(tableOptions: TableOptions.CreateIfNotExists);
            } else {
                services.AddDbContextFactory<Database>(options => options
                    .UseSqlite($"Data Source={path};Pooling=False")
                    .ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
                services.AddRepository<CustomInsightSource, EfCoreRepository<Database, CustomInsightSource>>();
                services.AddScoped<IUnitOfWork<Database>, EfCoreUnitOfWork<Database>>();

                var builder = new SchemataBuilder(new ConfigurationBuilder().Build(), null!);
                builder.UseInsight<CustomInsightSource>(insight => insight.UseDatabaseCatalog());
                builder.Invoke(services);
            }

            var root = services.BuildServiceProvider();
            if (!linq) {
                await using var database = await root.GetRequiredService<IDbContextFactory<Database>>().CreateDbContextAsync();
                await database.Database.EnsureCreatedAsync();
            }

            return new(root, path);
        }

        public async ValueTask DisposeAsync() {
            await services.DisposeAsync();
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) {
                File.Delete(path);
            }
        }
    }
}
