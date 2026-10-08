using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Schemata.Abstractions.Entities;
using Schemata.Entity.Repository;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;
using static Schemata.Tenancy.Tests.Fixtures.TenancyTestHost;
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
        var uow     = CommittingUnit();
        tenants.Setup(t => t.Begin()).Returns(uow.Object);

        hosts.Setup(h => h.ListAsync(It.IsAny<Func<IQueryable<SchemataTenantHost>, IQueryable<SchemataTenantHost>>>(),
                                     It.IsAny<CancellationToken>()))
             .Returns(EmptyAsync<SchemataTenantHost>());

        using var provider = CreateProvider(tenants, hosts, cache);
        var manager = Manager(provider);

        await manager.DeleteAsync(tenant, CancellationToken.None);

        cache.Verify(c => c.Remove(tenantId.ToString()), Times.Once);
    }

    [Fact]
    public async Task DeleteTenant_RemovesHostsAtomically() {
        var tenant = new SchemataTenant { Uid = Guid.NewGuid(), Name = "acme", CanonicalName = "tenants/acme" };
        var host   = new SchemataTenantHost { Uid = Guid.NewGuid(), Parent = "tenants/acme", Host = "a.test" };

        var tenants        = new Mock<IRepository<SchemataTenant>>();
        var hosts          = new Mock<IRepository<SchemataTenantHost>>();
        var cache          = new Mock<ITenantProviderCache>();
        var tenantMutation = Mutation<SchemataTenant>();
        var hostMutation   = Mutation<SchemataTenantHost>();
        var hostRemoved    = false;
        var tenantRemoved  = false;
        var uow = CommittingUnit(() => {
            Assert.True(hostRemoved);
            Assert.True(tenantRemoved);
        });
        tenants.Setup(t => t.Begin()).Returns(uow.Object);
        hosts.Setup(h => h.ListAsync(It.IsAny<Func<IQueryable<SchemataTenantHost>, IQueryable<SchemataTenantHost>>>(),
                                     It.IsAny<CancellationToken>()))
             .Returns(OneAsync(host));
        hostMutation.Setup(h => h.DeleteAsync(host, uow.Object, It.IsAny<Operations>(), It.IsAny<CancellationToken>()))
                    .Callback(() => hostRemoved = true)
                    .ReturnsAsync(MutationResult.Applied);
        tenantMutation.Setup(t => t.DeleteAsync(tenant, uow.Object, It.IsAny<Operations>(), It.IsAny<CancellationToken>()))
                      .Callback(() => tenantRemoved = true)
                      .ReturnsAsync(MutationResult.Applied);

        using var provider = CreateProvider(tenants, hosts, cache, tenantMutation, hostMutation);
        var manager = Manager(provider);

        await manager.DeleteAsync(tenant, CancellationToken.None);

        Assert.True(hostRemoved);
        Assert.True(tenantRemoved);
        hosts.Verify(h => h.Join(uow.Object), Times.Once);
        tenants.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
        hosts.Verify(h => h.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }


    private static async IAsyncEnumerable<T> OneAsync<T>(T item) {
        yield return item;
        await Task.CompletedTask;
    }

    [Fact]
    public async Task FindByHost_Resolves_Tenant_Through_Association_Table() {
        var tenantUid = Guid.NewGuid();
        var tenant = new SchemataTenant { Uid = tenantUid, Name = "acme", CanonicalName = "tenants/acme" };
        var host = new SchemataTenantHost {
            Uid = Guid.NewGuid(), Parent = "tenants/acme", Host = "example.test",
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

        using var provider = CreateProvider(tenants, hosts, cache);
        var manager = Manager(provider);

        var resolved = await manager.FindByHost("example.test", CancellationToken.None);

        Assert.NotNull(resolved);
        Assert.Equal(tenantUid, resolved.Uid);
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

        using var provider = CreateProvider(tenants, hosts, cache);
        var manager = Manager(provider);

        Assert.Null(await manager.FindByHost("missing.test", CancellationToken.None));
    }

    private static async IAsyncEnumerable<T> EmptyAsync<T>() {
        await Task.CompletedTask;
        yield break;
    }
}
