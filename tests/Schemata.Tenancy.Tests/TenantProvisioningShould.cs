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

public class TenantProvisioningShould
{
    [Trait("Layer", "Component")]
    [Fact]
    public async Task Create_Stages_Tenant_Through_Mutation_Owner() {
        var tenant         = Tenant("acme");
        var tenantMutation = Mutation<SchemataTenant>();
        var added          = false;
        tenantMutation.Setup(value => value.CreateAsync(tenant, null, It.IsAny<CancellationToken>()))
                      .Callback(() => added = true)
                      .ReturnsAsync(MutationResult.Applied);
        using var provider = CreateProvider(tenantMutation: tenantMutation);

        await Manager(provider).CreateAsync(tenant, CancellationToken.None);

        Assert.True(added);
    }

    [Fact]
    public async Task Update_Commits_Tenant_Then_Evicts_Cache() {
        var tenant         = Tenant("acme");
        var tenants        = new Mock<IRepository<SchemataTenant>>();
        var cache          = new Mock<ITenantProviderCache>();
        var tenantMutation = Mutation<SchemataTenant>();
        var committed      = false;
        var unit           = CommittingUnit(() => committed = true);
        tenants.Setup(value => value.Begin()).Returns(unit.Object);
        cache.Setup(value => value.Remove(tenant.Uid.ToString()))
             .Callback(() => Assert.True(committed));
        using var provider = CreateProvider(tenants, cache: cache, tenantMutation: tenantMutation);

        await Manager(provider).UpdateAsync(tenant, CancellationToken.None);

        unit.Verify(value => value.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        cache.Verify(value => value.Remove(tenant.Uid.ToString()), Times.Once);
    }

    [Fact]
    public async Task Delete_Removes_Hosts_And_Tenant_Atomically_Then_Evicts_Cache() {
        var tenant         = Tenant("acme");
        var host           = new SchemataTenantHost { Parent = tenant.CanonicalName, Host = "acme.test" };
        var tenants        = new Mock<IRepository<SchemataTenant>>();
        var hosts          = new Mock<IRepository<SchemataTenantHost>>();
        var cache          = new Mock<ITenantProviderCache>();
        var tenantMutation = Mutation<SchemataTenant>();
        var hostMutation   = Mutation<SchemataTenantHost>();
        var committed      = false;
        var hostRemoved    = false;
        var tenantRemoved  = false;
        var unit = CommittingUnit(() => {
            Assert.True(hostRemoved);
            Assert.True(tenantRemoved);
            committed = true;
        });
        tenants.Setup(value => value.Begin()).Returns(unit.Object);
        hosts.Setup(value => value.ListAsync(
                        It.IsAny<Func<IQueryable<SchemataTenantHost>, IQueryable<SchemataTenantHost>>>(),
                        It.IsAny<CancellationToken>()))
             .Returns(ToAsync([host]));
        hostMutation.Setup(value => value.DeleteAsync(host, unit.Object, It.IsAny<Operations>(), It.IsAny<CancellationToken>()))
                    .Callback(() => hostRemoved = true)
                    .ReturnsAsync(MutationResult.Applied);
        tenantMutation.Setup(value => value.DeleteAsync(tenant, unit.Object, It.IsAny<Operations>(), It.IsAny<CancellationToken>()))
                      .Callback(() => tenantRemoved = true)
                      .ReturnsAsync(MutationResult.Applied);
        cache.Setup(value => value.Remove(tenant.Uid.ToString()))
             .Callback(() => Assert.True(committed));
        using var provider = CreateProvider(tenants, hosts, cache, tenantMutation, hostMutation);

        await Manager(provider).DeleteAsync(tenant, CancellationToken.None);

        hosts.Verify(value => value.Join(unit.Object), Times.Once);
        unit.Verify(value => value.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        cache.Verify(value => value.Remove(tenant.Uid.ToString()), Times.Once);
    }

    [Fact]
    public async Task SetDisplayName_Assigns_Value_In_Place() {
        var tenant = Tenant("acme");
        using var provider = CreateProvider();

        await Manager(provider).SetDisplayNameAsync(tenant, "Acme Europe", CancellationToken.None);

        Assert.Equal("Acme Europe", tenant.DisplayName);
    }

    [Fact]
    public async Task SetDisplayNames_Assigns_Nonempty_Map_And_Collapses_Empty_Map() {
        var tenant = Tenant("acme");
        var names = new Dictionary<string, string?> { ["en"] = "Acme", ["fr"] = null };
        using var provider = CreateProvider();
        var manager = Manager(provider);

        await manager.SetDisplayNamesAsync(tenant, names, CancellationToken.None);

        var assigned = Assert.IsType<Dictionary<string, string?>>(tenant.DisplayNames);
        Assert.Equal("Acme", assigned["en"]);
        Assert.True(assigned.ContainsKey("fr"));
        Assert.Null(assigned["fr"]);

        await manager.SetDisplayNamesAsync(tenant, [], CancellationToken.None);

        Assert.Null(tenant.DisplayNames);
    }

    [Fact]
    public async Task SetHosts_Replaces_Existing_Rows_With_Normalized_Hosts() {
        var tenant         = Tenant("acme");
        var oldHost        = new SchemataTenantHost { Parent = tenant.CanonicalName, Host = "old.test" };
        var added          = new List<SchemataTenantHost>();
        var removed        = false;
        var hosts          = new Mock<IRepository<SchemataTenantHost>>();
        var tenants        = new Mock<IRepository<SchemataTenant>>();
        var tenantMutation = Mutation<SchemataTenant>();
        var hostMutation   = Mutation<SchemataTenantHost>();
        var unit = CommittingUnit(() => {
            Assert.True(removed);
            Assert.Equal(2, added.Count);
        });
        tenants.Setup(value => value.Begin()).Returns(unit.Object);
        hosts.Setup(value => value.ListAsync(
                        It.IsAny<Func<IQueryable<SchemataTenantHost>, IQueryable<SchemataTenantHost>>>(),
                        It.IsAny<CancellationToken>()))
             .Returns(ToAsync([oldHost]));
        hostMutation.Setup(value => value.DeleteAsync(oldHost, unit.Object, It.IsAny<Operations>(), It.IsAny<CancellationToken>()))
                    .Callback(() => removed = true)
                    .ReturnsAsync(MutationResult.Applied);
        hostMutation.Setup(value => value.CreateAsync(It.IsAny<SchemataTenantHost>(), unit.Object, It.IsAny<CancellationToken>()))
                    .Callback((SchemataTenantHost row, IUnitOfWork? _, CancellationToken _) => added.Add(row))
                    .ReturnsAsync(MutationResult.Applied);
        using var provider = CreateProvider(tenants, hosts, tenantMutation: tenantMutation, hostMutation: hostMutation);

        await Manager(provider).SetHostsAsync(
            tenant,
            [" One.TEST ", "", "two.test"],
            CancellationToken.None);

        Assert.True(removed);
        Assert.Equal(["one.test", "two.test"], added.Select(row => row.Host));
        Assert.All(added, row => Assert.Equal(tenant.CanonicalName, row.Parent));
        hosts.Verify(value => value.Join(unit.Object), Times.Once);
        unit.Verify(value => value.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetHosts_Empty_Removes_Existing_Rows_Without_Adding_Replacements() {
        var tenant       = Tenant("acme");
        var oldHost      = new SchemataTenantHost { Parent = tenant.CanonicalName, Host = "old.test" };
        var hosts        = new Mock<IRepository<SchemataTenantHost>>(MockBehavior.Strict);
        var tenants      = new Mock<IRepository<SchemataTenant>>();
        var hostMutation = Mutation<SchemataTenantHost>();
        var unit         = CommittingUnit();
        var removed      = false;
        hostMutation.Setup(value => value.DeleteAsync(oldHost, unit.Object, It.IsAny<Operations>(), It.IsAny<CancellationToken>()))
                    .Callback(() => removed = true)
                    .ReturnsAsync(MutationResult.Applied);
        tenants.Setup(value => value.Begin()).Returns(unit.Object);
        hosts.Setup(value => value.Join(unit.Object));
        hosts.Setup(value => value.ListAsync(
                        It.IsAny<Func<IQueryable<SchemataTenantHost>, IQueryable<SchemataTenantHost>>>(),
                        It.IsAny<CancellationToken>()))
             .Returns(ToAsync([oldHost]));
        using var provider = CreateProvider(tenants, hosts, hostMutation: hostMutation);

        await Manager(provider).SetHostsAsync(tenant, [], CancellationToken.None);

        Assert.True(removed);
        hostMutation.Verify(
            value => value.CreateAsync(It.IsAny<SchemataTenantHost>(), It.IsAny<IUnitOfWork?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        unit.Verify(value => value.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FindByTenantId_Returns_Only_Matching_Tenant() {
        var expected = Tenant("acme");
        var other    = Tenant("other");
        var tenants  = new Mock<IRepository<SchemataTenant>>();
        tenants.Setup(value => value.SingleOrDefaultAsync(
                          It.IsAny<Func<IQueryable<SchemataTenant>, IQueryable<SchemataTenant>>>(),
                          It.IsAny<CancellationToken>()))
               .Returns((Func<IQueryable<SchemataTenant>, IQueryable<SchemataTenant>> query, CancellationToken _) =>
                   ValueTask.FromResult<SchemataTenant?>(query(new[] { expected, other }.AsQueryable()).SingleOrDefault()));
        using var provider = CreateProvider(tenants);

        var result = await Manager(provider).FindByTenantId(expected.Uid, CancellationToken.None);

        Assert.Same(expected, result);
    }

    [Fact]
    public async Task FindByHost_Normalizes_Host_And_Resolves_Associated_Tenant() {
        var expected = Tenant("acme");
        var hosts = new Mock<IRepository<SchemataTenantHost>>();
        hosts.Setup(value => value.SingleOrDefaultAsync(
                        It.IsAny<Func<IQueryable<SchemataTenantHost>, IQueryable<SchemataTenantHost>>>(),
                        It.IsAny<CancellationToken>()))
             .Returns((Func<IQueryable<SchemataTenantHost>, IQueryable<SchemataTenantHost>> query, CancellationToken _) =>
                 ValueTask.FromResult<SchemataTenantHost?>(query(new[] {
                     new SchemataTenantHost { Parent = expected.CanonicalName, Host = "acme.test" },
                 }.AsQueryable()).SingleOrDefault()));
        var tenants = new Mock<IRepository<SchemataTenant>>();
        tenants.Setup(value => value.SingleOrDefaultAsync(
                          It.IsAny<Func<IQueryable<SchemataTenant>, IQueryable<SchemataTenant>>>(),
                          It.IsAny<CancellationToken>()))
               .Returns((Func<IQueryable<SchemataTenant>, IQueryable<SchemataTenant>> query, CancellationToken _) =>
                   ValueTask.FromResult<SchemataTenant?>(query(new[] { expected }.AsQueryable()).SingleOrDefault()));
        using var provider = CreateProvider(tenants, hosts);

        var result = await Manager(provider).FindByHost(" ACME.TEST ", CancellationToken.None);

        Assert.Same(expected, result);
    }

    [Fact]
    public async Task FindByHost_Blank_Returns_Null_Without_Querying_Repositories() {
        var tenants = new Mock<IRepository<SchemataTenant>>(MockBehavior.Strict);
        var hosts   = new Mock<IRepository<SchemataTenantHost>>(MockBehavior.Strict);
        using var provider = CreateProvider(tenants, hosts);

        var result = await Manager(provider).FindByHost(" ", CancellationToken.None);

        Assert.Null(result);
        hosts.Verify(value => value.SingleOrDefaultAsync(
                         It.IsAny<Func<IQueryable<SchemataTenantHost>, IQueryable<SchemataTenantHost>>>(),
                         It.IsAny<CancellationToken>()), Times.Never);
        tenants.Verify(value => value.SingleOrDefaultAsync(
                           It.IsAny<Func<IQueryable<SchemataTenant>, IQueryable<SchemataTenant>>>(),
                           It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetHosts_Returns_Associated_Nonblank_Hosts_In_Order() {
        var tenant = Tenant("acme");
        var rows = new[] {
            new SchemataTenantHost { Parent = tenant.CanonicalName, Host = "one.test" },
            new SchemataTenantHost { Parent = tenant.CanonicalName, Host = " " },
            new SchemataTenantHost { Parent = "other", Host = "other.test" },
            new SchemataTenantHost { Parent = tenant.CanonicalName, Host = "two.test" },
        };
        var hosts = new Mock<IRepository<SchemataTenantHost>>();
        hosts.Setup(value => value.ListAsync(
                        It.IsAny<Func<IQueryable<SchemataTenantHost>, IQueryable<SchemataTenantHost>>>(),
                        It.IsAny<CancellationToken>()))
             .Returns((Func<IQueryable<SchemataTenantHost>, IQueryable<SchemataTenantHost>> query, CancellationToken _) =>
                 ToAsync(query(rows.AsQueryable()).ToArray()));
        using var provider = CreateProvider(hosts: hosts);

        var result = await Manager(provider).GetHostsAsync(tenant, CancellationToken.None);

        Assert.Equal(new[] { "one.test", "two.test" }, result.ToArray());
    }

    private static SchemataTenant Tenant(string name) {
        return new() { Uid = Guid.NewGuid(), Name = name, CanonicalName = "tenants/" + name };
    }

    private static async IAsyncEnumerable<T> ToAsync<T>(IEnumerable<T> rows) {
        foreach (var row in rows) {
            yield return row;
        }

        await Task.CompletedTask;
    }
}
