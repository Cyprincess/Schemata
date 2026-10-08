using System;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Tenancy;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository;
using Xunit;

namespace Schemata.Tenancy.Caching.Integration.Tests;

[Trait("Layer", "Integration")]
public class TenantQueryCacheShould
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Active_Join_Preserves_Other_Tenant_Cache_And_Rollback_Generation(bool commit) {
        var path = Path.Combine(Path.GetTempPath(), $"tenant-query-{Guid.NewGuid():N}.db");
        var services = new ServiceCollection();
        services.AddDbContextFactory<CacheContext>(options => options.UseSqlite($"Data Source={path}"));
        services.AddMemoryCacheProvider();
        services.AddTenantCache();
        services.AddRepository<Row, EfCoreRepository<CacheContext, Row>>().UseQueryCache();
        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IDbContextFactory<CacheContext>>();
        var a = new TenantIdentity(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var b = new TenantIdentity(Guid.Parse("22222222-2222-2222-2222-222222222222"));
        try {
            await using (var db = await factory.CreateDbContextAsync()) {
                await db.Database.EnsureCreatedAsync();
                db.Rows.AddRange(new Row { Id = 1, Value = "A original" }, new Row { Id = 2, Value = "B original" });
                await db.SaveChangesAsync();
            }
            using (TenantContext.Enter(a)) Assert.Equal("A original", await ReadAsync(provider, 1));
            using (TenantContext.Enter(b)) Assert.Equal("B original", await ReadAsync(provider, 2));

            // A raw write leaves B's shared result available to detect an accidental cross-tenant eviction.
            await using (var db = await factory.CreateDbContextAsync()) {
                await db.Rows.Where(row => row.Id == 2).ExecuteUpdateAsync(set => set.SetProperty(row => row.Value, "B SQL"));
            }
            using (TenantContext.Enter(a)) {
                using var scope = provider.CreateScope();
                using var writer = scope.ServiceProvider.GetRequiredService<IRepository<Row>>();
                using var reader = scope.ServiceProvider.GetRequiredService<IRepository<Row>>();
                using var uow = writer.Begin();
                reader.Join(uow);
                var db = ((IUnitOfWork<CacheContext>)uow).Context;
                await using var sql = await db.Database.BeginTransactionAsync();
                var row = (await writer.FirstOrDefaultAsync(q => q.Where(item => item.Id == 1)))!;
                row.Value = "A transaction";
                await writer.UpdateAsync(row);
                await db.SaveChangesAsync();
                Assert.Equal("A transaction", await reader.FirstOrDefaultAsync(q => q.Where(item => item.Id == 1).Select(item => item.Value)));
                if (commit) {
                    await sql.CommitAsync();
                    await uow.CommitAsync();
                } else {
                    await sql.RollbackAsync();
                    await uow.RollbackAsync();
                    await using var independent = await factory.CreateDbContextAsync();
                    await independent.Rows.Where(item => item.Id == 1).ExecuteUpdateAsync(set => set.SetProperty(item => item.Value, "A SQL"));
                }
                Assert.Equal(commit ? "A transaction" : "A original", await ReadAsync(provider, 1));
            }
            using (TenantContext.Enter(b)) Assert.Equal("B original", await ReadAsync(provider, 2));
        } finally {
            await using var db = await factory.CreateDbContextAsync();
            await db.Database.EnsureDeletedAsync();
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static async Task<string?> ReadAsync(IServiceProvider provider, int id) {
        using var scope = provider.CreateScope();
        using var repository = scope.ServiceProvider.GetRequiredService<IRepository<Row>>();
        return await repository.FirstOrDefaultAsync(q => q.Where(row => row.Id == id).Select(row => row.Value));
    }

    public sealed class Row
    {
        [Key]
        public int Id { get; set; }
        public string? Value { get; set; }
    }

    public sealed class CacheContext(DbContextOptions<CacheContext> options) : DbContext(options)
    {
        public DbSet<Row> Rows => Set<Row>();
    }
}
