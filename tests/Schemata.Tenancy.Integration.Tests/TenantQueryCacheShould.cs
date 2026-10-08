using System;
using Microsoft.AspNetCore.Builder;
using System.ComponentModel.DataAnnotations.Schema;
using System.IO;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Tenancy;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.Repository;
using Xunit;

namespace Schemata.Tenancy.Integration.Tests;

[Trait("Layer", "Integration")]
public class TenantQueryCacheShould
{
    [Fact]
    public async Task Commit_Advances_Only_Current_Tenant_Generation_For_Identical_Query() {
        var path = Path.Combine(Path.GetTempPath(), "schemata-tenant-cache-" + Guid.NewGuid().ToString("N") + ".db");
        var a = new TenantIdentity(Guid.NewGuid());
        var b = new TenantIdentity(Guid.NewGuid());
        try {
            var services = new ServiceCollection();
            services.AddMemoryCacheProvider().AddTenantCache();
            services.AddDbContextFactory<Database>(options => options.UseSqlite($"Data Source={path};Pooling=False").ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
            services.AddRepository<Row, EfCoreRepository<Database, Row>>().UseQueryCache();
            await using var root = services.BuildServiceProvider();
            await using (var setup = root.CreateAsyncScope()) {
                var db = setup.ServiceProvider.GetRequiredService<Database>();
                await db.Database.EnsureCreatedAsync();
                db.Add(new Row { Uid = Guid.NewGuid(), Value = "initial" });
                await db.SaveChangesAsync();
            }
            async Task<string?> Read(TenantIdentity identity) {
                using var frame = TenantContext.Enter(identity);
                await using var scope = root.CreateAsyncScope();
                return (await scope.ServiceProvider.GetRequiredService<IRepository<Row>>().FirstOrDefaultAsync<Row>(null))?.Value;
            }
            Assert.Equal("initial", await Read(a));
            Assert.Equal("initial", await Read(b));
            using (TenantContext.Enter(a)) {
                await using var scope = root.CreateAsyncScope();
                var repository = scope.ServiceProvider.GetRequiredService<IRepository<Row>>();
                var row = (await repository.FirstOrDefaultAsync<Row>(null))!;
                row.Value = "committed";
                await repository.UpdateAsync(row);
                await repository.CommitAsync();
            }
            Assert.Equal("committed", await Read(a));
            Assert.Equal("initial", await Read(b));
            Assert.Equal("committed", await Read(TenantIdentity.Host));
        } finally {
            File.Delete(path);
        }
    }

    [Schemata.Abstractions.Entities.PrimaryKey(nameof(Uid))]
    public sealed class Row : IIdentifier
    {
        public Guid Uid { get; set; }
        public string Value { get; set; } = null!;
    }
    public sealed class Database(DbContextOptions<Database> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder model) { model.Entity<Row>(); base.OnModelCreating(model); }
    }
}
