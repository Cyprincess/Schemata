using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Schemata.Common;
using Schemata.Entity.Repository;
using Schemata.Tenancy.Foundation.Services;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;
using Xunit;

namespace Schemata.Tenancy.Tests;

public class SchemataTenantManagerShould
{
    [Fact]
    public async Task DeleteAsync_Evicts_Tenant_Provider_From_Cache() {
        var tenantId = Identifiers.NewUid();
        var tenant   = new SchemataTenant { Uid = tenantId };

        var tenants = new Mock<IRepository<SchemataTenant>>();
        var hosts   = new Mock<IRepository<SchemataTenantHost>>();
        var cache   = new Mock<ITenantProviderCache>();
        var uow     = UnitOfWork();
        tenants.Setup(t => t.Begin()).Returns(uow.Object);

        hosts.Setup(h => h.ListAsync(It.IsAny<Func<IQueryable<SchemataTenantHost>, IQueryable<SchemataTenantHost>>>(),
                                     It.IsAny<CancellationToken>()))
             .Returns(EmptyAsync<SchemataTenantHost>());

        var manager = new SchemataTenantManager<SchemataTenant>(tenants.Object, hosts.Object, cache.Object);

        await manager.DeleteAsync(tenant, CancellationToken.None);

        cache.Verify(c => c.Remove(tenantId.ToString()), Times.Once);
    }

    [Fact]
    public async Task DeleteTenant_RemovesHostsAtomically() {
        var tenant = new SchemataTenant { Uid     = Identifiers.NewUid(), Name   = "acme" };
        var host   = new SchemataTenantHost { Uid = Identifiers.NewUid(), Tenant = "acme", Host = "a.test" };

        var tenants = new Mock<IRepository<SchemataTenant>>();
        var hosts   = new Mock<IRepository<SchemataTenantHost>>();
        var cache   = new Mock<ITenantProviderCache>();
        var uow     = UnitOfWork();
        tenants.Setup(t => t.Begin()).Returns(uow.Object);
        tenants.Setup(t => t.RemoveAsync(It.IsAny<SchemataTenant>(), It.IsAny<CancellationToken>()))
               .Returns(Task.CompletedTask);
        hosts.Setup(h => h.ListAsync(It.IsAny<Func<IQueryable<SchemataTenantHost>, IQueryable<SchemataTenantHost>>>(),
                                     It.IsAny<CancellationToken>()))
             .Returns(OneAsync(host));
        hosts.Setup(h => h.RemoveRangeAsync(It.IsAny<IEnumerable<SchemataTenantHost>>(), It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);

        var manager = new SchemataTenantManager<SchemataTenant>(tenants.Object, hosts.Object, cache.Object);

        await manager.DeleteAsync(tenant, CancellationToken.None);

        hosts.Verify(h => h.Join(uow.Object), Times.Once);
        hosts.Verify(
            h => h.RemoveRangeAsync(It.Is<IEnumerable<SchemataTenantHost>>(e => e.Contains(host)),
                                    It.IsAny<CancellationToken>()), Times.Once);
        tenants.Verify(t => t.RemoveAsync(tenant, It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        tenants.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
        hosts.Verify(h => h.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private static Mock<IUnitOfWork> UnitOfWork() {
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        uow.Setup(u => u.DisposeAsync()).Returns(ValueTask.CompletedTask);
        return uow;
    }

    private static async IAsyncEnumerable<T> OneAsync<T>(T item) {
        yield return item;
        await Task.CompletedTask;
    }

    [Fact]
    public async Task FindByHost_Resolves_Tenant_Through_Association_Table() {
        var tenantUid = Identifiers.NewUid();
        var tenant    = new SchemataTenant { Uid = tenantUid, Name = "acme" };
        var host = new SchemataTenantHost {
            Uid = Identifiers.NewUid(), Tenant = "acme", Host = "example.test",
        };

        var tenants = new Mock<IRepository<SchemataTenant>>();
        var hosts   = new Mock<IRepository<SchemataTenantHost>>();
        var cache   = new Mock<ITenantProviderCache>();

        hosts.Setup(h => h.SingleOrDefaultAsync(
                        It.IsAny<Func<IQueryable<SchemataTenantHost>, IQueryable<SchemataTenantHost>>>(),
                        It.IsAny<CancellationToken>()))
             .ReturnsAsync(host);

        tenants.Setup(t => t.SingleOrDefaultAsync(
                          It.IsAny<Func<IQueryable<SchemataTenant>, IQueryable<SchemataTenant>>>(),
                          It.IsAny<CancellationToken>()))
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
                        It.IsAny<CancellationToken>()))
             .ReturnsAsync((SchemataTenantHost?)null);

        var manager = new SchemataTenantManager<SchemataTenant>(tenants.Object, hosts.Object, cache.Object);

        Assert.Null(await manager.FindByHost("missing.test", CancellationToken.None));
    }

    private static async IAsyncEnumerable<T> EmptyAsync<T>() {
        await Task.CompletedTask;
        yield break;
    }
}
