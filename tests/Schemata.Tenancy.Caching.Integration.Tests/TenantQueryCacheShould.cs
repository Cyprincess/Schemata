using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Entities;
using Schemata.Caching.Memory;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository;
using Schemata.Tenancy.Foundation.Repository;
using Xunit;
using PrimaryKeyAttribute = Schemata.Abstractions.Entities.PrimaryKeyAttribute;

namespace Schemata.Tenancy.Caching.Integration.Tests;

[Trait("Layer", "Integration")]
public class TenantQueryCacheShould
{
    private static readonly Guid TenantA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task Commit_Evicts_Only_Its_Own_Source_And_Preserves_Other_Tenant_Results() {
        var path = Path.Combine(Path.GetTempPath(), $"tenant-query-cache-{Guid.NewGuid():N}.db");
        try {
            await using var store = TenancyCacheStore.Create(
                services => services
                    .AddMemoryCacheProvider()
                    .AddDbContextFactory<Database>(options => options.UseSqlite($"Data Source={path};Pooling=False")
                        .ReplaceService<IModelCustomizer, SchemataModelCustomizer>())
                    .AddRepository<Row, EfCoreRepository<Database, Row>>().UseQueryCache(),
                builder => builder
                    .UseRepositories(repositories => {
                        repositories.Services.AddTransient<IRepository<Row>, EfCoreRepository<Database, Row>>();
                        repositories.Services.AddDbContextFactory<Database>(options => options.UseSqlite($"Data Source={path};Pooling=False")
                            .ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
                    })
                    .UseCache(cache => cache.UseShared(new MemoryCacheBackend())));
            await using (var database = await store.Root.GetRequiredService<IDbContextFactory<Database>>().CreateDbContextAsync()) {
                await database.Database.EnsureCreatedAsync();
                database.Rows.Add(new Row { Uid = Guid.NewGuid(), TenantUid = TenantA, Name = "a", Value = "a original", Timestamp = Guid.NewGuid() });
                database.Rows.Add(new Row { Uid = Guid.NewGuid(), TenantUid = TenantB, Name = "b", Value = "b original", Timestamp = Guid.NewGuid() });
                await database.SaveChangesAsync();
            }

            Assert.Equal("a original", await ReadValueAsync(store, TenantA, "a"));
            Assert.Equal("b original", await ReadValueAsync(store, TenantB, "b"));

            await using (var scope = await store.EnterAsync(TenantA)) {
                await using var repository = scope.ServiceProvider.GetRequiredService<IRepository<Row>>();
                var row = Assert.Single(await repository.ListAsync(query => query).ToListAsync(), item => item.Name == "a");
                row.Value = "a committed";
                Assert.Equal(MutationResult.Applied, await repository.UpdateAsync(row));
                await repository.CommitAsync();
            }

            // The raw write bypasses every eviction: if B's result survives A's commit below, it
            // was served from B's untouched generation, not re-read from the database.
            await using (var database = await store.Root.GetRequiredService<IDbContextFactory<Database>>().CreateDbContextAsync()) {
                await database.Rows.Where(item => item.Name == "b").ExecuteUpdateAsync(set => set.SetProperty(item => item.Value, "b raw"));
            }

            Assert.Equal("b original", await ReadValueAsync(store, TenantB, "b"));
            Assert.Equal("a committed", await ReadValueAsync(store, TenantA, "a"));

            await using (var scope = await store.EnterAsync(TenantB)) {
                await using var repository = scope.ServiceProvider.GetRequiredService<IRepository<Row>>();
                var row = Assert.Single(await repository.ListAsync(query => query).ToListAsync(), item => item.Name == "b");
                row.Value = "b committed";
                Assert.Equal(MutationResult.Applied, await repository.UpdateAsync(row));
                await repository.CommitAsync();
            }

            Assert.Equal("b committed", await ReadValueAsync(store, TenantB, "b"));
        } finally {
            File.Delete(path);
        }
    }

    private static async Task<string?> ReadValueAsync(TenancyCacheStore store, Guid tenant, string name) {
        await using var scope = await store.EnterAsync(tenant);
        await using var repository = scope.ServiceProvider.GetRequiredService<IRepository<Row>>();
        return (await repository.FirstOrDefaultAsync<Row>(query => query.Where(row => row.Name == name)))?.Value;
    }

    [Table("Rows")]
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
}
