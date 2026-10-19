using System;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;
using Xunit;

namespace Schemata.Tenancy.Integration.Tests;

[Trait("Layer", "Integration")]
public class TenantRollbackShould
{
    [Fact]
    public async Task Host_Insert_Failure_Rolls_Back_Removal_And_Tenant_Version() {
        using var database = new SqliteConnection("Data Source=:memory:");
        await database.OpenAsync();
        await using var root = TenantStorageShould.Build(database);
        var tenant = new SchemataTenant { Name = "rollback" };
        await using (var scope = root.CreateAsyncScope()) {
            var db = scope.ServiceProvider.GetRequiredService<TenantStorageShould.Database>();
            await db.Database.EnsureCreatedAsync();
            var manager = scope.ServiceProvider.GetRequiredService<ITenantManager<SchemataTenant>>();
            await manager.CreateAsync(tenant, default);
            await manager.SetHostsAsync(tenant, ["old.test"], default);
            await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER reject_host BEFORE INSERT ON SchemataTenantHosts WHEN NEW.Host = 'rejected.test' BEGIN SELECT RAISE(ABORT, 'rejected host'); END;");
        }
        var version = tenant.Timestamp;
        await using (var scope = root.CreateAsyncScope()) {
            var manager = scope.ServiceProvider.GetRequiredService<ITenantManager<SchemataTenant>>();
            await Assert.ThrowsAnyAsync<Exception>(() => manager.SetHostsAsync(tenant, ["rejected.test"], default).AsTask());
        }
        await using var verification = root.CreateAsyncScope();
        var stored = await verification.ServiceProvider.GetRequiredService<TenantStorageShould.Database>().Set<SchemataTenant>().SingleAsync();
        var host = await verification.ServiceProvider.GetRequiredService<TenantStorageShould.Database>().Set<SchemataTenantHost>().SingleAsync();
        Assert.Equal(version, stored.Timestamp);
        Assert.Equal("old.test", host.Host);
        Assert.Equal(tenant.CanonicalName, host.Parent);
    }
}
