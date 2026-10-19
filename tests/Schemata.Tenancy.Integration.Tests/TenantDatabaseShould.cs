using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LinqToDB;
using LinqToDB.Data;
using LinqToDB.Mapping;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Entities;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.LinqToDB;
using Schemata.Entity.Repository;
using Schemata.Tenancy.Foundation;
using Schemata.Tenancy.Foundation.Repository;
using Schemata.Tenancy.Foundation.Services;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;
using Xunit;
using PrimaryKeyAttribute = Schemata.Abstractions.Entities.PrimaryKeyAttribute;

namespace Schemata.Tenancy.Integration.Tests;

[Trait("Layer", "Integration")]
public sealed class TenantDatabaseShould
{
    private static readonly Guid TenantA = new("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TenantB = new("00000000-0000-0000-0000-000000000002");
    private static readonly Guid TenantC = new("00000000-0000-0000-0000-000000000003");

    private enum TenantDatabaseMode
    {
        SeparateFiles, SharedFileForDifferentTenants, MissingSource,
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Same_Uid_Lands_In_Each_Tenants_Own_Database_And_Leaves_Global_Unchanged(bool linq) {
        Assert.False(typeof(ITenant).IsAssignableFrom(typeof(Row)));
        await using var store = await Store.CreateAsync(linq, TenantDatabaseMode.SeparateFiles);
        await store.InitializeTenantAsync(TenantA);
        await store.InitializeTenantAsync(TenantB);

        var uid = Guid.NewGuid();
        await using (var scopeA = await store.EnterAsync(TenantA)) {
            var rows = scopeA.ServiceProvider.GetRequiredService<IRepository<Row>>();
            Assert.Equal(MutationResult.Applied, await rows.AddAsync(new Row { Uid = uid, Name = "a", Value = "A" }));
            await rows.CommitAsync();
        }
        await using (var scopeB = await store.EnterAsync(TenantB)) {
            var rows = scopeB.ServiceProvider.GetRequiredService<IRepository<Row>>();
            Assert.Equal(MutationResult.Applied, await rows.AddAsync(new Row { Uid = uid, Name = "b", Value = "B" }));
            await rows.CommitAsync();
        }

        await using var a = await store.EnterAsync(TenantA);
        Assert.Equal("A", (await a.ServiceProvider.GetRequiredService<IRepository<Row>>().FindAsync(new object[] { uid }))!.Value);
        await using var b = await store.EnterAsync(TenantB);
        Assert.Equal("B", (await b.ServiceProvider.GetRequiredService<IRepository<Row>>().FindAsync(new object[] { uid }))!.Value);
        await using var global = await store.EnterAsync(null);
        Assert.Null(await global.ServiceProvider.GetRequiredService<IRepository<Row>>().FindAsync(new object[] { uid }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_Tenant_Source_Configuration_Fails_The_Tenant(bool linq) {
        await using var store = await Store.CreateAsync(linq, TenantDatabaseMode.MissingSource);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () => await store.EnterAsync(TenantA));
        Assert.Contains("source", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Join_Across_Different_Databases_Is_Rejected_And_The_Owner_Keeps_Its_Row(bool linq) {
        await using var store = await Store.CreateAsync(linq, TenantDatabaseMode.SeparateFiles);
        await store.InitializeTenantAsync(TenantA);
        await store.InitializeTenantAsync(TenantB);

        var uid = Guid.NewGuid();
        await using (var a = await store.EnterAsync(TenantA)) {
            var rows = a.ServiceProvider.GetRequiredService<IRepository<Row>>();
            Assert.Equal(MutationResult.Applied, await rows.AddAsync(new Row { Uid = uid, Name = "a", Value = "A" }));
            await rows.CommitAsync();
        }

        await using (var a = await store.EnterAsync(TenantA)) {
            await using var b = await store.EnterAsync(TenantB);
            var repositoryA = a.ServiceProvider.GetRequiredService<IRepository<Row>>();
            IUnitOfWork unitOfWorkB = linq
                ? b.ServiceProvider.GetRequiredService<IUnitOfWork<Connection>>()
                : b.ServiceProvider.GetRequiredService<IUnitOfWork<Database>>();

            var error = Assert.Throws<InvalidOperationException>(() => repositoryA.Join(unitOfWorkB));
            Assert.Contains("source", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("A", (await repositoryA.FindAsync(new object[] { uid }))!.Value);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Join_On_The_Same_File_With_A_Different_Tenant_Binding_Is_Rejected(bool linq) {
        await using var store = await Store.CreateAsync(linq, TenantDatabaseMode.SharedFileForDifferentTenants);
        // Tenants A and C bind the same physical file, so the schema is initialized once.
        await store.InitializeTenantAsync(TenantA);

        var uid = Guid.NewGuid();
        await using (var a = await store.EnterAsync(TenantA)) {
            var rows = a.ServiceProvider.GetRequiredService<IRepository<Row>>();
            Assert.Equal(MutationResult.Applied, await rows.AddAsync(new Row { Uid = uid, Name = "a", Value = "A" }));
            await rows.CommitAsync();
        }

        await using (var a = await store.EnterAsync(TenantA)) {
            await using var c = await store.EnterAsync(TenantC);
            var repositoryA = a.ServiceProvider.GetRequiredService<IRepository<Row>>();
            IUnitOfWork unitOfWorkC = linq
                ? c.ServiceProvider.GetRequiredService<IUnitOfWork<Connection>>()
                : c.ServiceProvider.GetRequiredService<IUnitOfWork<Database>>();

            var error = Assert.Throws<InvalidOperationException>(() => repositoryA.Join(unitOfWorkC));
            Assert.Contains("source", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("A", (await repositoryA.FindAsync(new object[] { uid }))!.Value);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Rollback_With_Multiple_Repositories_Under_The_Same_Binding_Rolls_All_Back(bool linq) {
        await using var store = await Store.CreateAsync(linq, TenantDatabaseMode.SeparateFiles);
        await store.InitializeTenantAsync(TenantA);

        var rowUid   = Guid.NewGuid();
        var thingUid = Guid.NewGuid();
        await using (var scope = await store.EnterAsync(TenantA)) {
            var rows   = scope.ServiceProvider.GetRequiredService<IRepository<Row>>();
            var things = scope.ServiceProvider.GetRequiredService<IRepository<Thing>>();
            IUnitOfWork unitOfWork = linq
                ? scope.ServiceProvider.GetRequiredService<IUnitOfWork<Connection>>()
                : scope.ServiceProvider.GetRequiredService<IUnitOfWork<Database>>();

            rows.Join(unitOfWork);
            things.Join(unitOfWork);

            Assert.Equal(MutationResult.Applied, await rows.AddAsync(new Row { Uid = rowUid, Name = "staged", Value = "staged" }));
            Assert.Equal(MutationResult.Applied, await things.AddAsync(new Thing { Uid = thingUid, Name = "staged" }));

            await unitOfWork.RollbackAsync();
        }

        await using (var scope = await store.EnterAsync(TenantA)) {
            Assert.Null(await scope.ServiceProvider.GetRequiredService<IRepository<Row>>().FindAsync(new object[] { rowUid }));
            Assert.Null(await scope.ServiceProvider.GetRequiredService<IRepository<Thing>>().FindAsync(new object[] { thingUid }));
        }
    }

    [System.ComponentModel.DataAnnotations.Schema.Table("Rows")]
    [PrimaryKey(nameof(Uid))]
    [CanonicalName("records/{record}")]
    public sealed class Row : IIdentifier, ICanonicalName, IConcurrency
    {
        public Guid Uid { get; set; }
        public string? Name { get; set; }
        public string? CanonicalName { get; set; }
        public string? Value { get; set; }
        [ConcurrencyCheck]
        public Guid Timestamp { get; set; }
    }

    [System.ComponentModel.DataAnnotations.Schema.Table("Things")]
    [PrimaryKey(nameof(Uid))]
    public sealed class Thing : IIdentifier
    {
        public Guid Uid { get; set; }
        public string? Name { get; set; }
    }

    public sealed class Database(DbContextOptions<Database> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder model) {
            model.Entity<Row>();
            model.Entity<Thing>();
        }
    }

    public sealed class Connection(DataOptions options) : DataConnection(options);

    private sealed class Store : IAsyncDisposable
    {
        private readonly bool                                   _linq;
        private readonly string                                 _directory;
        private readonly Dictionary<Guid, SchemataTenant>       _tenants;
        private readonly MemoryCacheTenantProviderCache         _cache;
        private readonly ServiceProvider                        _root;
        private readonly SchemataTenantServiceScopeFactory<SchemataTenant> _scopes;

        private Store(
            bool                                               linq,
            string                                             directory,
            Dictionary<Guid, SchemataTenant>                   tenants,
            MemoryCacheTenantProviderCache                     cache,
            ServiceProvider                                    root,
            SchemataTenantServiceScopeFactory<SchemataTenant>  scopes
        ) {
            _linq      = linq;
            _directory = directory;
            _tenants   = tenants;
            _cache     = cache;
            _root      = root;
            _scopes    = scopes;
        }

        public static async Task<Store> CreateAsync(bool linq, TenantDatabaseMode mode) {
            var directory = Path.Combine(Path.GetTempPath(), $"schemata-tenant-database-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            var globalPath = Path.Combine(directory, "global.db");
            var sharedPath = Path.Combine(directory, "shared.db");

            var tenants = new Dictionary<Guid, SchemataTenant>();
            var manager = new Mock<ITenantManager<SchemataTenant>>();
            manager.Setup(value => value.FindByTenantId(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .Returns((Guid identifier, CancellationToken _) => new ValueTask<SchemataTenant?>(
                    tenants.TryGetValue(identifier, out var found) ? found : null));

            var services = new ServiceCollection();
            services.AddSingleton<ITenantManager<SchemataTenant>>(manager.Object);

            string PathFor(Guid uid) {
                return mode switch {
                    TenantDatabaseMode.SharedFileForDifferentTenants => uid == TenantA || uid == TenantC ? sharedPath : Path.Combine(directory, $"{uid:N}.db"),
                    _                                                => Path.Combine(directory, $"{uid:N}.db"),
                };
            }

            if (linq) {
                var mapping = new MappingSchema();
                mapping.AddMetadataReader(new SystemComponentModelDataAnnotationsSchemaAttributeReader());
                var globalOptions = new DataOptions().UseSQLite($"Data Source={globalPath};Pooling=False").UseMappingSchema(mapping);
                services.AddSingleton<Func<Connection>>(_ => () => new(globalOptions));
                services.AddRepository<Row, LinqToDbRepository<Connection, Row>>();
                services.AddRepository<Thing, LinqToDbRepository<Connection, Thing>>();
                new SchemataTenancyBuilder<SchemataTenant>(services).UseRepositories(builder => {
                    var path = PathFor(builder.TenantUid);
                    if (mode is TenantDatabaseMode.MissingSource) {
                        builder.UseDatabase<SchemataTenant, Row, LinqToDbRepository<Connection, Row>>(_ => { });
                        return;
                    }
                    builder.UseDatabase<SchemataTenant, Row, LinqToDbRepository<Connection, Row>>(tenant
                        => tenant.AddRepositorySource<Connection>((_, options) => options.UseSQLite($"Data Source={path};Pooling=False")));
                    builder.UseDatabase<SchemataTenant, Thing, LinqToDbRepository<Connection, Thing>>(tenant
                        => tenant.AddRepositorySource<Connection>((_, options) => options.UseSQLite($"Data Source={path};Pooling=False")));
                });
            } else {
                services.AddDbContextFactory<Database>(options => options.UseSqlite($"Data Source={globalPath};Pooling=False")
                    .ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
                services.AddRepository<Row, EfCoreRepository<Database, Row>>();
                services.AddRepository<Thing, EfCoreRepository<Database, Thing>>();
                new SchemataTenancyBuilder<SchemataTenant>(services).UseRepositories(builder => {
                    var path = PathFor(builder.TenantUid);
                    if (mode is TenantDatabaseMode.MissingSource) {
                        builder.UseDatabase<SchemataTenant, Row, EfCoreRepository<Database, Row>>(_ => { });
                        return;
                    }
                    builder.UseDatabase<SchemataTenant, Row, EfCoreRepository<Database, Row>>(tenant
                        => tenant.AddRepositorySource<Database>((_, options) => options.UseSqlite($"Data Source={path};Pooling=False")
                            .ReplaceService<IModelCustomizer, SchemataModelCustomizer>()));
                    builder.UseDatabase<SchemataTenant, Thing, EfCoreRepository<Database, Thing>>(tenant
                        => tenant.AddRepositorySource<Database>((_, options) => options.UseSqlite($"Data Source={path};Pooling=False")
                            .ReplaceService<IModelCustomizer, SchemataModelCustomizer>()));
                });
            }

            var root = services.BuildServiceProvider();
            try {
                var tenancy = root.GetRequiredService<IOptions<SchemataTenancyOptions>>();
                var cache = new MemoryCacheTenantProviderCache(tenancy);
                var providers = new SchemataTenantServiceProviderFactory<SchemataTenant>(root, cache, tenancy);
                var store = new Store(linq, directory, tenants, cache, root, new(root, providers));

                if (linq) {
                    using var database = root.GetRequiredService<Func<Connection>>()();
                    database.CreateTable<Row>();
                    database.CreateTable<Thing>();
                } else {
                    await using var database = await root.GetRequiredService<IDbContextFactory<Database>>().CreateDbContextAsync();
                    await database.Database.EnsureCreatedAsync();
                }

                return store;
            } catch {
                await root.DisposeAsync();
                throw;
            }
        }

        public async Task InitializeTenantAsync(Guid uid) {
            await using var scope = await EnterAsync(uid);
            if (_linq) {
                using var database = scope.ServiceProvider.GetRequiredService<Func<Connection>>()();
                database.CreateTable<Row>();
                database.CreateTable<Thing>();
            } else {
                await using var database = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<Database>>().CreateDbContextAsync();
                await database.Database.EnsureCreatedAsync();
            }
        }

        // The fixture catalog resolves every requested identifier as an existing tenant.
        public ValueTask<AsyncServiceScope> EnterAsync(Guid? tenant) {
            if (tenant is { } uid) Register(uid);
            return _scopes.CreateAsync(new(tenant));
        }

        private SchemataTenant Register(Guid uid) {
            if (_tenants.TryGetValue(uid, out var existing)) return existing;
            var tenant = new SchemataTenant { Uid = uid, Timestamp = Guid.NewGuid(), Name = uid.ToString() };
            _tenants[uid] = tenant;
            return tenant;
        }

        public async ValueTask DisposeAsync() {
            await _cache.DisposeAsync();
            await _root.DisposeAsync();
            Directory.Delete(_directory, true);
        }
    }
}
