using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Schemata.Entity.Repository;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;
using Schemata.Tenancy.Skeleton.Services;
using Xunit;

namespace Schemata.Tenancy.Tests;

public class SchemataTenantManagerShould
{
    [Fact]
    public async Task DeleteAsync_Evicts_Tenant_Provider_From_Cache() {
        var tenantId = Guid.NewGuid();
        var tenant   = new SchemataTenant { Uid = tenantId };

        var tenants = new Mock<IRepository<SchemataTenant>>();
        var hosts   = new Mock<IRepository<SchemataTenantHost>>();
        var cache   = new Mock<ITenantProviderCache>();

        var manager = new SchemataTenantManager<SchemataTenant>(tenants.Object, hosts.Object, cache.Object);

        await manager.DeleteAsync(tenant, CancellationToken.None);

        cache.Verify(c => c.Remove(tenantId.ToString()), Times.Once);
    }

    [Fact]
    public async Task FindByHost_Resolves_Tenant_Through_Association_Table() {
        var tenantUid = Guid.NewGuid();
        var tenant    = new SchemataTenant { Uid = tenantUid };
        var host = new SchemataTenantHost {
            Uid = Guid.NewGuid(), SchemataTenantUid = tenantUid, Host = "example.test",
        };

        var tenants = new Mock<IRepository<SchemataTenant>>();
        var hosts   = new Mock<IRepository<SchemataTenantHost>>();
        var cache   = new Mock<ITenantProviderCache>();

        hosts.Setup(h => h.SingleOrDefaultAsync(
                        It.IsAny<Func<IQueryable<SchemataTenantHost>, IQueryable<SchemataTenantHost>>>(),
                        It.IsAny<CancellationToken>()
                    )
              )
             .ReturnsAsync(host);

        tenants.Setup(t => t.SingleOrDefaultAsync(
                          It.IsAny<Func<IQueryable<SchemataTenant>, IQueryable<SchemataTenant>>>(),
                          It.IsAny<CancellationToken>()
                      )
                )
               .ReturnsAsync(tenant);

        var manager = new SchemataTenantManager<SchemataTenant>(tenants.Object, hosts.Object, cache.Object);

        var resolved = await manager.FindByHost("example.test", CancellationToken.None);

        Assert.NotNull(resolved);
        Assert.Equal(tenantUid, resolved!.Uid);
    }

    [Fact]
    public async Task FindByHost_Returns_Null_When_No_Host_Row_Matches() {
        var tenants = new Mock<IRepository<SchemataTenant>>();
        var hosts   = new Mock<IRepository<SchemataTenantHost>>();
        var cache   = new Mock<ITenantProviderCache>();

        hosts.Setup(h => h.SingleOrDefaultAsync(
                        It.IsAny<Func<IQueryable<SchemataTenantHost>, IQueryable<SchemataTenantHost>>>(),
                        It.IsAny<CancellationToken>()
                    )
              )
             .ReturnsAsync((SchemataTenantHost?)null);

        var manager = new SchemataTenantManager<SchemataTenant>(tenants.Object, hosts.Object, cache.Object);

        Assert.Null(await manager.FindByHost("missing.test", CancellationToken.None));
    }
}
