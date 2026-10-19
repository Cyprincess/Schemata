using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.IO;
using System.Linq;
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
using Schemata.Caching.Memory;
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
public class TenantQueryCacheShould
{
    private static readonly Guid TenantA = new("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TenantB = new("00000000-0000-0000-0000-000000000002");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Commit_Advances_Only_The_Committing_Source_Generation(bool linq) {
        var path = Path.Combine(Path.GetTempPath(), $"schemata-tenant-query-cache-{Guid.NewGuid():N}.db");
        try {
            var tenants = new Dictionary<Guid, SchemataTenant>();
            var manager = new Mock<ITenantManager<SchemataTenant>>();
            manager.Setup(value => value.FindByTenantId(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .Returns((Guid identifier, CancellationToken _) => new ValueTask<SchemataTenant?>(
                    tenants.TryGetValue(identifier, out var found) ? found : null));
            var services = new ServiceCollection();
            services.AddSingleton(manager.Object);
            services.AddMemoryCacheProvider();
            if (linq) {
                var mapping = new MappingSchema();
                mapping.AddMetadataReader(new SystemComponentModelDataAnnotationsSchemaAttributeReader());
                var options = new DataOptions().UseSQLite($"Data Source={path};Pooling=False").UseMappingSchema(mapping);
                services.AddSingleton<Func<Connection>>(_ => () => new(options));
                services.AddRepository<Row, LinqToDbRepository<Connection, Row>>().UseQueryCache();
                new SchemataTenancyBuilder<SchemataTenant>(services)
                    .UseRepositories(builder => {
                        builder.Services.AddTransient<IRepository<Row>, LinqToDbRepository<Connection, Row>>();
                        builder.Services.AddSingleton<Func<Connection>>(_ => () => new(options));
                    })
                    .UseCache(cache => cache.UseShared(new MemoryCacheBackend()));
            } else {
                services.AddDbContextFactory<Database>(options => options.UseSqlite($"Data Source={path};Pooling=False")
                    .ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
                services.AddRepository<Row, EfCoreRepository<Database, Row>>().UseQueryCache();
                new SchemataTenancyBuilder<SchemataTenant>(services)
                    .UseRepositories(builder => {
                        builder.Services.AddTransient<IRepository<Row>, EfCoreRepository<Database, Row>>();
                        builder.Services.AddDbContextFactory<Database>(options => options.UseSqlite($"Data Source={path};Pooling=False")
                            .ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
                    })
                    .UseCache(cache => cache.UseShared(new MemoryCacheBackend()));
            }

            await using var root = services.BuildServiceProvider();
            var tenancy = root.GetRequiredService<IOptions<SchemataTenancyOptions>>();
            using var providers = new MemoryCacheTenantProviderCache(tenancy);
            var scopes = new SchemataTenantServiceScopeFactory<SchemataTenant>(
                root, new SchemataTenantServiceProviderFactory<SchemataTenant>(root, providers, tenancy));

            async Task<AsyncServiceScope> EnterAsync(Guid? tenant) {
                if (tenant is { } uid && !tenants.ContainsKey(uid)) {
                    tenants[uid] = new() { Uid = uid, Timestamp = Guid.NewGuid(), Name = uid.ToString() };
                }

                return await scopes.CreateAsync(new(tenant));
            }
            async Task<List<string?>> ReadAsync(Guid? tenant) {
                var scope = await EnterAsync(tenant);
                await using (scope) {
                    await using var repository = scope.ServiceProvider.GetRequiredService<IRepository<Row>>();
                    return await repository.ListAsync(query => query.Select(row => row.Value)).ToListAsync();
                }
            }
            async Task<string?> ReadCachedAsync(Guid? tenant, string name) {
                var scope = await EnterAsync(tenant);
                await using (scope) {
                    await using var repository = scope.ServiceProvider.GetRequiredService<IRepository<Row>>();
                    return (await repository.FirstOrDefaultAsync<Row>(query => query.Where(row => row.Name == name)))?.Value;
                }
            }
            async Task CommitAsync(Guid tenant, string name, string value) {
                var scope = await EnterAsync(tenant);
                await using (scope) {
                    await using var repository = scope.ServiceProvider.GetRequiredService<IRepository<Row>>();
                    var row = Assert.Single(await repository.ListAsync(query => query).ToListAsync(), item => item.Name == name);
                    row.Value = value;
                    Assert.Equal(MutationResult.Applied, await repository.UpdateAsync(row));
                    await repository.CommitAsync();
                }
            }
            async Task SeedAsync() {
                if (linq) {
                    using var database = root.GetRequiredService<Func<Connection>>()();
                    database.CreateTable<Row>();
                    database.Insert(new Row { Uid = Guid.NewGuid(), Name = "host", Value = "host original", Timestamp = Guid.NewGuid() });
                    database.Insert(new Row { Uid = Guid.NewGuid(), TenantUid = TenantA, Name = "a", Value = "a original", Timestamp = Guid.NewGuid() });
                    database.Insert(new Row { Uid = Guid.NewGuid(), TenantUid = TenantB, Name = "b", Value = "b original", Timestamp = Guid.NewGuid() });
                } else {
                    await using var database = await root.GetRequiredService<IDbContextFactory<Database>>().CreateDbContextAsync();
                    await database.Database.EnsureCreatedAsync();
                    database.Rows.Add(new Row { Uid = Guid.NewGuid(), Name = "host", Value = "host original", Timestamp = Guid.NewGuid() });
                    database.Rows.Add(new Row { Uid = Guid.NewGuid(), TenantUid = TenantA, Name = "a", Value = "a original", Timestamp = Guid.NewGuid() });
                    database.Rows.Add(new Row { Uid = Guid.NewGuid(), TenantUid = TenantB, Name = "b", Value = "b original", Timestamp = Guid.NewGuid() });
                    await database.SaveChangesAsync();
                }
            }
            async Task WriteRawAsync(string name, string value) {
                if (linq) {
                    using var database = root.GetRequiredService<Func<Connection>>()();
                    await database.GetTable<Row>().Where(item => item.Name == name).Set(item => item.Value, value).UpdateAsync();
                } else {
                    await using var database = await root.GetRequiredService<IDbContextFactory<Database>>().CreateDbContextAsync();
                    await database.Rows.Where(item => item.Name == name).ExecuteUpdateAsync(set => set.SetProperty(item => item.Value, value));
                }
            }

            await SeedAsync();
            // Tenant-registered entities are partitioned: the host scope reads the host
            // partition, tenant scopes read their own.
            Assert.Equal(new[] { "host original" }, await ReadAsync(null));
            Assert.Equal("a original", await ReadCachedAsync(TenantA, "a"));
            Assert.Equal("b original", await ReadCachedAsync(TenantB, "b"));
            Assert.Equal("host original", await ReadCachedAsync(null, "host"));

            await CommitAsync(TenantA, "a", "a committed");
            await WriteRawAsync("b", "b raw");
            await WriteRawAsync("host", "host raw");

            // The raw writes bypass every eviction: the unchanged reads below were served from
            // generations A's commit could not touch — B's tenant source and the host partition's.
            Assert.Equal("a committed", await ReadCachedAsync(TenantA, "a"));
            Assert.Equal("b original", await ReadCachedAsync(TenantB, "b"));
            Assert.Equal("host original", await ReadCachedAsync(null, "host"));
        } finally {
            File.Delete(path);
        }
    }

    [System.ComponentModel.DataAnnotations.Schema.Table("Rows")]
    [PrimaryKey(nameof(Uid))]
    public sealed class Row : ITenant, IIdentifier, IConcurrency
    {
        public Guid Uid { get; set; }
        public Guid? TenantUid { get; set; }
        public string? Name { get; set; }
        public string? Value { get; set; }
        [ConcurrencyCheck]
        public Guid Timestamp { get; set; }
    }

    public sealed class Database(DbContextOptions<Database> options) : DbContext(options)
    {
        public DbSet<Row> Rows => Set<Row>();
    }

    public sealed class Connection(DataOptions options) : DataConnection(options);

}
