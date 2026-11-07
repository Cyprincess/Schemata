using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Flow.Integration.Tests.Fixtures;
using Schemata.Flow.Skeleton.Entities;
using Xunit;

namespace Schemata.Flow.Integration.Tests;

public sealed class FlowTenantRepositoryShould
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Isolate_All_Persisted_Rows_With_Same_Business_Keys_And_Reject_Foreign_Writes(bool linq) {
        var fixture = new TenantFlowFixture(linq);
        var lifecycle = (IAsyncLifetime)fixture;
        await lifecycle.InitializeAsync();
        try {
            using (var scope = fixture.CreateScope()) {
                Assert.Null(scope.ServiceProvider.GetService<IRepository<SchemataProcess>>());
                Assert.NotNull(scope.ServiceProvider.GetService<IRepository<TenantProcess>>());
            }
            await Exercise(() => new TenantProcess { Name = "shared", DefinitionName = "test", DefinitionVersion = "1", IdempotencyKey = "shared" });
            await Exercise(() => new TenantProcessToken { Name = "shared", Process = "processes/shared", ScopeName = string.Empty, StateName = "task", State = "Active" });
            await Exercise(() => new TenantProcessTransition { Name = "shared", Process = "processes/shared", Event = "advance" });
            await Exercise(() => new TenantProcessParticipant { Process = "processes/shared", Subject = "users/shared" });
            await Exercise(() => new TenantProcessSource { Process = "processes/shared", Name = "shared", SourceType = "Order", Source = "orders/shared" });
            await Exercise(() => new TenantProcessCompensation { Name = "shared", Process = "processes/shared", ScopeOwnerCanonicalName = "processes/shared/tokens/shared", ActivityName = "undo", BoundaryName = "boundary" });
            await Exercise(() => new TenantFlowEffectIntent { RequestId = "processes/shared/tokens/shared/effects/0", Process = "processes/shared", Token = "processes/shared/tokens/shared", Task = "task" });
        } finally {
            await lifecycle.DisposeAsync();
        }

        async Task Exercise<TEntity>(Func<TEntity> create) where TEntity : class, ITenant, IIdentifier, IConcurrency {
            var first = Guid.NewGuid();
            var second = Guid.NewGuid();
            var foreign = await Seed(first);
            var own = await Seed(second);
            var host = await Seed(null);
            foreach (var identity in new Guid?[] { first, second, null }) {
                await using var scope = await fixture.EnterTenantAsync(identity);
                var repository = scope.ServiceProvider.GetRequiredService<IRepository<TEntity>>();
                var rows = await repository.ListAsync(q => q).ToListAsync();
                // Host scope reads the host partition; tenant scopes read their own.
                Assert.Equal(identity == first ? foreign.Uid : identity == second ? own.Uid : host.Uid, Assert.Single(rows).Uid);
            }
            await using (var scope = await fixture.EnterTenantAsync(second)) {
                var repository = scope.ServiceProvider.GetRequiredService<IRepository<TEntity>>();
                Assert.Null(await repository.FindAsync([foreign.Uid], default, new QueryOptions(IgnoreOwner: true)));
                await Assert.ThrowsAsync<PermissionDeniedException>(async () => {
                    await repository.UpdateAsync(foreign);
                    await repository.CommitAsync();
                });
            }
            await using (var scope = await fixture.EnterTenantAsync(first)) {
                var repository = scope.ServiceProvider.GetRequiredService<IRepository<TEntity>>();
                var persisted = await repository.FindAsync([foreign.Uid]);
                Assert.NotNull(persisted);
                Assert.Equal(first, persisted.TenantUid);
                Assert.Equal(foreign.Timestamp, persisted.Timestamp);
            }

            async Task<TEntity> Seed(Guid? tenant) {
                await using var scope = await fixture.EnterTenantAsync(tenant);
                var repository = scope.ServiceProvider.GetRequiredService<IRepository<TEntity>>();
                var row = create();
                await repository.AddAsync(row);
                await repository.CommitAsync();
                Assert.Equal(tenant, row.TenantUid);
                return row;
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Allow_Duplicate_Partition_Keys_Through_Direct_Writes(bool linq) {
        var fixture = new TenantFlowFixture(linq);
        var lifecycle = (IAsyncLifetime)fixture;
        await lifecycle.InitializeAsync();
        try {
            foreach (var tenant in new Guid?[] { null, Guid.NewGuid() }) {
                await AllowDuplicate(tenant, () => new TenantProcess { DefinitionName = "unique", DefinitionVersion = "1", IdempotencyKey = "key" });
                await AllowDuplicate(tenant, () => new TenantProcessSource { Process = "processes/unique", Name = "process-source", SourceType = "Order", Source = "orders/unique" });
                await AllowDuplicate(tenant, () => new TenantProcessSource { Process = "processes/unique", Token = "processes/unique/tokens/unique", Name = "token-source", SourceType = "Order", Source = "orders/unique" });
                await AllowDuplicate(tenant, () => new TenantFlowEffectIntent { RequestId = "processes/unique/tokens/unique/effects/0", Process = "processes/unique", Token = "processes/unique/tokens/unique", Task = "task" });
            }
        } finally {
            await lifecycle.DisposeAsync();
        }

        async Task AllowDuplicate<TEntity>(Guid? tenant, Func<TEntity> create) where TEntity : class, IIdentifier {
            TEntity first;
            TEntity second;
            await using (var scope = await fixture.EnterTenantAsync(tenant)) {
                var repository = scope.ServiceProvider.GetRequiredService<IRepository<TEntity>>();
                first = create();
                await repository.AddAsync(first);
                await repository.CommitAsync();
            }
            await using (var scope = await fixture.EnterTenantAsync(tenant)) {
                var repository = scope.ServiceProvider.GetRequiredService<IRepository<TEntity>>();
                second = create();
                await repository.AddAsync(second);
                await repository.CommitAsync();
            }
            Assert.NotEqual(first.Uid, second.Uid);
            await using (var scope = await fixture.EnterTenantAsync(tenant)) {
                var repository = scope.ServiceProvider.GetRequiredService<IRepository<TEntity>>();
                Assert.NotNull(await repository.FindAsync([first.Uid]));
                Assert.NotNull(await repository.FindAsync([second.Uid]));
            }
        }
    }
}
