using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using LinqToDB;
using LinqToDB.Data;
using LinqToDB.Mapping;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Abstractions.Advisors;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.EntityFrameworkCore;
using Schemata.Entity.LinqToDB;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Tenancy.Foundation;
using Schemata.Tenancy.Foundation.Services;
using Schemata.Tenancy.Skeleton;
using Schemata.Tenancy.Skeleton.Entities;
using Schemata.Tenancy.Skeleton.Tenancy;
using Xunit;
using PrimaryKeyAttribute = Schemata.Abstractions.Entities.PrimaryKeyAttribute;

namespace Schemata.Tenancy.Integration.Tests;

[Trait("Layer", "Integration")]
public sealed class TenantRepositoryShould
{
    private static readonly Guid TenantA = new("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TenantB = new("00000000-0000-0000-0000-000000000002");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Stamp_Bound_Tenant_And_Query_Only_Its_Rows(bool linq) {
        await using var store = await Store.CreateAsync(linq);
        await store.AddAsync(null, "host");
        await store.AddAsync(TenantA, "a");
        await store.AddAsync(TenantB, "b");
        foreach (var (tenant, expected) in new (Guid, string)[] { (TenantA, "a"), (TenantB, "b") }) {
            await using var scope = await store.EnterAsync(tenant);
            await using var repository = scope.ServiceProvider.GetRequiredService<IRepository<Row>>();
            var rows = await repository.ListAsync(query => query).ToListAsync();
            var row = Assert.Single(rows);
            Assert.Equal(expected, row.Name);
            Assert.Equal(tenant, row.TenantUid);
            Assert.Equal($"records/{expected}", row.CanonicalName);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Repository_Bound_To_A_Keeps_Writing_And_Reading_A_Under_Ambient_B(bool linq) {
        await using var store = await Store.CreateAsync(linq);
        await using var scopeA = await store.EnterAsync(TenantA);
        await using var scopeB = await store.EnterAsync(TenantB);
        await using var repositoryBoundToA = scopeA.ServiceProvider.GetRequiredService<IRepository<Row>>();
        await using var repositoryBoundToB = scopeB.ServiceProvider.GetRequiredService<IRepository<Row>>();
        var row = new Row { Name = "a-only", TenantUid = TenantB };
        using (TenantContext.Enter(new(TenantB))) {
            Assert.Equal(MutationResult.Applied, await repositoryBoundToA.AddAsync(row));
            await repositoryBoundToA.CommitAsync();
        }
        Assert.Equal(TenantA, row.TenantUid);
        Assert.Null(await repositoryBoundToB.FindAsync(new object[] { row.Uid }));
        Assert.Equal("a-only", (await repositoryBoundToA.FindAsync(new object[] { row.Uid }))!.Name);
        using (TenantContext.Enter(new(TenantB))) {
            var rows = await repositoryBoundToA.ListAsync(query => query).ToListAsync();
            Assert.Equal("a-only", Assert.Single(rows).Name);
            Assert.Equal(TenantA, rows[0].TenantUid);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Host_Global_Repository_Keeps_Global_Scope_Under_Ambient_Tenants(bool linq) {
        await using var store = await Store.CreateAsync(linq);
        await store.AddAsync(TenantA, "a");
        await using var scope = await store.EnterAsync(null);
        await using var repository = scope.ServiceProvider.GetRequiredService<IRepository<Row>>();
        var row = new Row { Name = "global" };
        using (TenantContext.Enter(new(TenantA))) {
            Assert.Equal(MutationResult.Applied, await repository.AddAsync(row));
            await repository.CommitAsync();
        }
        Assert.Null(row.TenantUid);
        using (TenantContext.Enter(new(TenantB))) {
            Assert.Equal("global", (await repository.FindAsync(new object[] { row.Uid }))!.Name);
        }
        await using var tenantScope = await store.EnterAsync(TenantA);
        await using var tenantRepository = tenantScope.ServiceProvider.GetRequiredService<IRepository<Row>>();
        Assert.Null(await tenantRepository.FindAsync(new object[] { row.Uid }));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Reject_Cross_Tenant_Mutation_Instead_Of_Returning_NoWrite(bool linq, bool remove) {
        await using var store = await Store.CreateAsync(linq);
        var row = await store.AddAsync(TenantA, "owned");
        await using var scope = await store.EnterAsync(TenantB);
        await using var repository = scope.ServiceProvider.GetRequiredService<IRepository<Row>>();
        await Assert.ThrowsAsync<PermissionDeniedException>(() => remove ? repository.RemoveAsync(row) : repository.UpdateAsync(row));
        await using var owner = await store.EnterAsync(TenantA);
        await using var owned = owner.ServiceProvider.GetRequiredService<IRepository<Row>>();
        Assert.Equal("owned", (await owned.GetAsync(row))!.Name);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public async Task Reject_Forged_Staged_Tenant_Against_Persisted_Row(bool linq, bool remove, bool forgedNull) {
        await using var store = await Store.CreateAsync(linq);
        Guid? forged = forgedNull ? null : TenantB;
        var row = await store.AddAsync(TenantA, "owned");
        row.TenantUid = forged;
        await using var scope = await store.EnterAsync(TenantA);
        await using var repository = scope.ServiceProvider.GetRequiredService<IRepository<Row>>();
        await Assert.ThrowsAsync<PermissionDeniedException>(() => remove ? repository.RemoveAsync(row) : repository.UpdateAsync(row));
        Assert.Equal(TenantA, row.TenantUid);
        await using var reader = await store.EnterAsync(TenantA);
        await using var readRepository = reader.ServiceProvider.GetRequiredService<IRepository<Row>>();
        Assert.Equal(TenantA, (await readRepository.GetAsync(row))!.TenantUid);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reject_Reparenting_A_Loaded_Row(bool linq) {
        await using var store = await Store.CreateAsync(linq);
        var row = await store.AddAsync(TenantA, "owned");
        await using var scope = await store.EnterAsync(TenantA);
        await using var repository = scope.ServiceProvider.GetRequiredService<IRepository<Row>>();
        var loaded = (await repository.GetAsync(row))!;
        loaded.TenantUid = null;
        await Assert.ThrowsAsync<PermissionDeniedException>(() => repository.UpdateAsync(loaded));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Restore_Persisted_Tenant_After_Denied_Reparent_In_Enlisted_Unit_Of_Work(bool linq) {
        await using var store = await Store.CreateAsync(linq);
        var row = await store.AddAsync(TenantA, "owned");
        await using var scope = await store.EnterAsync(TenantA);
        await using var repository = scope.ServiceProvider.GetRequiredService<IRepository<Row>>();
        await using var transaction = repository.Begin();
        var loaded = (await repository.GetAsync(row))!;
        loaded.TenantUid = null;
        await Assert.ThrowsAsync<PermissionDeniedException>(() => repository.UpdateAsync(loaded));
        Assert.Equal(TenantA, loaded.TenantUid);
        await repository.AddAsync(new Row { Name = "next" });
        await transaction.CommitAsync();
        await using var reader = await store.EnterAsync(TenantA);
        await using var readRepository = reader.ServiceProvider.GetRequiredService<IRepository<Row>>();
        Assert.Equal(TenantA, (await readRepository.GetAsync(row))!.TenantUid);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Roll_Back_Caller_Unit_Of_Work_When_Forged_Row_Has_No_Owned_Original(bool linq) {
        await using var store = await Store.CreateAsync(linq);
        var row = await store.AddAsync(TenantA, "owned");
        await using (var scope = await store.EnterAsync(TenantB)) {
            await using var repository = scope.ServiceProvider.GetRequiredService<IRepository<Row>>();
            await using var transaction = repository.Begin();
            await repository.AddAsync(new Row { Name = "rollback" });
            row.TenantUid = TenantB;
            await Assert.ThrowsAsync<PermissionDeniedException>(() => repository.UpdateAsync(row));
            await transaction.RollbackAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() => transaction.CommitAsync());
        }
        await using var reader = await store.EnterAsync(TenantB);
        await using var readRepository = reader.ServiceProvider.GetRequiredService<IRepository<Row>>();
        Assert.False(await readRepository.AnyAsync(query => query));
        await using var owner = await store.EnterAsync(TenantA);
        await using var ownRepository = owner.ServiceProvider.GetRequiredService<IRepository<Row>>();
        Assert.Equal(TenantA, (await ownRepository.GetAsync(row))!.TenantUid);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Same_Tenant_Update_And_Remove_Report_Applied_And_Persist(bool linq) {
        await using var store = await Store.CreateAsync(linq);
        var row = await store.AddAsync(TenantA, "owned");
        await using (var scope = await store.EnterAsync(TenantA)) {
            await using var repository = scope.ServiceProvider.GetRequiredService<IRepository<Row>>();
            row.Value = "changed";
            Assert.Equal(MutationResult.Applied, await repository.UpdateAsync(row));
            await repository.CommitAsync();
        }
        await using (var scope = await store.EnterAsync(TenantA)) {
            await using var repository = scope.ServiceProvider.GetRequiredService<IRepository<Row>>();
            var current = (await repository.GetAsync(row))!;
            Assert.Equal("changed", current.Value);
            Assert.Equal(MutationResult.Applied, await repository.RemoveAsync(current));
            await repository.CommitAsync();
        }
        await using var reader = await store.EnterAsync(TenantA);
        await using var readRepository = reader.ServiceProvider.GetRequiredService<IRepository<Row>>();
        Assert.Null(await readRepository.GetAsync(row));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Reject_Missing_Persisted_Row_Instead_Of_Reporting_Applied(bool linq, bool remove) {
        await using var store = await Store.CreateAsync(linq);
        await using var scope = await store.EnterAsync(TenantA);
        await using var repository = scope.ServiceProvider.GetRequiredService<IRepository<Row>>();
        var row = new Row { Uid = Guid.NewGuid() };
        await Assert.ThrowsAsync<PermissionDeniedException>(() => remove ? repository.RemoveAsync(row) : repository.UpdateAsync(row));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Persist_Internal_Tenant_Scalar_Without_Exposing_It_In_Json(bool linq) {
        await using var store = await Store.CreateAsync(linq);
        var row = await store.AddAsync(TenantA, "private");
        await using var scope = await store.EnterAsync(TenantA);
        await using var repository = scope.ServiceProvider.GetRequiredService<IRepository<Row>>();
        var persisted = (await repository.GetAsync(row))!;
        Assert.Equal(TenantA, persisted.TenantUid);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(persisted));
        Assert.False(json.RootElement.TryGetProperty(nameof(Row.TenantUid), out _));
        Assert.Equal("private", json.RootElement.GetProperty(nameof(Row.Name)).GetString());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Reject_Stale_Tenant_Mutation_After_Concurrent_Write(bool linq, bool remove) {
        await using var store = await Store.CreateAsync(linq);
        var stale = await store.AddAsync(TenantA, "shared");
        await using (var scope = await store.EnterAsync(TenantA)) {
            await using var repository = scope.ServiceProvider.GetRequiredService<IRepository<Row>>();
            var winner = (await repository.GetAsync(stale))!;
            winner.Value = "winner";
            Assert.Equal(MutationResult.Applied, await repository.UpdateAsync(winner));
            await repository.CommitAsync();
        }
        await using (var scope = await store.EnterAsync(TenantA)) {
            await using var repository = scope.ServiceProvider.GetRequiredService<IRepository<Row>>();
            stale.Value = "stale";
            await Assert.ThrowsAsync<AbortedException>(async () => {
                if (remove) await repository.RemoveAsync(stale);
                else await repository.UpdateAsync(stale);
                await repository.CommitAsync();
            });
        }
        await using var reader = await store.EnterAsync(TenantA);
        await using var readRepository = reader.ServiceProvider.GetRequiredService<IRepository<Row>>();
        Assert.Equal("winner", (await readRepository.GetAsync(stale))!.Value);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Reject_InFlight_Concurrent_Write_With_Stable_Uid_And_Token(bool linq, bool remove) {
        var inFlight = 0;
        Func<Row, Task<AdviseResult>> inflight = _ => throw new InvalidOperationException("In-flight writer was not installed.");
        await using var store = await Store.CreateAsync(linq, services => {
            if (remove) {
                var advisor = new Mock<IRepositoryRemoveAdvisor<Row>>();
                advisor.Setup(value => value.Order).Returns(int.MaxValue);
                advisor.Setup(value => value.AdviseAsync(It.IsAny<AdviceContext>(), It.IsAny<IRepository<Row>>(), It.IsAny<Row>(), It.IsAny<MutationOptions>(), It.IsAny<CancellationToken>()))
                    .Returns((AdviceContext _, IRepository<Row> _, Row entity, MutationOptions _, CancellationToken _) => inflight(entity));
                services.AddSingleton(advisor.Object);
            } else {
                var advisor = new Mock<IRepositoryUpdateAdvisor<Row>>();
                advisor.Setup(value => value.Order).Returns(int.MaxValue);
                advisor.Setup(value => value.AdviseAsync(It.IsAny<AdviceContext>(), It.IsAny<IRepository<Row>>(), It.IsAny<Row>(), It.IsAny<MutationOptions>(), It.IsAny<CancellationToken>()))
                    .Returns((AdviceContext _, IRepository<Row> _, Row entity, MutationOptions _, CancellationToken _) => inflight(entity));
                services.AddSingleton(advisor.Object);
            }
        });
        var original = await store.AddAsync(TenantA, "shared");

        inflight = async stale => {
            if (Interlocked.Exchange(ref inFlight, 1) != 0) {
                return AdviseResult.Continue;
            }
            await using var scope = await store.EnterAsync(TenantA);
            await using var repository = scope.ServiceProvider.GetRequiredService<IRepository<Row>>();
            var winner = (await repository.FindAsync(new object[] { stale.Uid }))!;
            winner.Value = "winner";
            await repository.UpdateAsync(winner);
            await repository.CommitAsync();
            return AdviseResult.Continue;
        };

        await using (var scope = await store.EnterAsync(TenantA)) {
            await using var repository = scope.ServiceProvider.GetRequiredService<IRepository<Row>>();
            await Assert.ThrowsAsync<AbortedException>(async () => {
                if (remove) await repository.RemoveAsync(original);
                else await repository.UpdateAsync(original);
                await repository.CommitAsync();
            });
        }

        await using var reader = await store.EnterAsync(TenantA);
        await using var readRepository = reader.ServiceProvider.GetRequiredService<IRepository<Row>>();
        var winner = (await readRepository.GetAsync(original))!;
        Assert.Equal(TenantA, winner.TenantUid);
        Assert.Equal("winner", winner.Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Add_Then_Update_Through_Different_Transient_Repositories_In_Shared_Unit(bool linq) {
        var row = new Row { Name = "staged", Value = "first" };
        var updates = new Mock<IRepositoryUpdateAdvisor<Row>>();
        updates.Setup(value => value.Order).Returns(int.MaxValue);
        updates.Setup(value => value.AdviseAsync(It.IsAny<AdviceContext>(), It.IsAny<IRepository<Row>>(), row, It.IsAny<MutationOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AdviseResult.Continue);
        await using var store = await Store.CreateAsync(linq, services => services.AddSingleton(updates.Object));
        await using (var scope = await store.EnterAsync(TenantA)) {
            await using var writer = scope.ServiceProvider.GetRequiredService<IRepository<Row>>();
            await using var transaction = writer.Begin();
            Assert.Equal(MutationResult.Applied, await writer.AddAsync(row));
            await using var updater = scope.ServiceProvider.GetRequiredService<IRepository<Row>>();
            Assert.NotSame(writer, updater);
            updater.Join(transaction);
            var original = await updater.GetOriginalAsync(row, value => value.TenantUid);
            Assert.True(original.Found);
            Assert.Equal(TenantA, original.Value);
            row.Value = "final";
            Assert.Equal(MutationResult.Applied, await updater.UpdateAsync(row));
            updates.Verify(value => value.AdviseAsync(It.IsAny<AdviceContext>(), updater, row, It.IsAny<MutationOptions>(), It.IsAny<CancellationToken>()), Times.Once);
            await transaction.CommitAsync();
        }
        await using var reader = await store.EnterAsync(TenantA);
        await using var repository = reader.ServiceProvider.GetRequiredService<IRepository<Row>>();
        var persisted = (await repository.GetAsync(row))!;
        Assert.Equal(TenantA, persisted.TenantUid);
        Assert.Equal("final", persisted.Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Restore_Admission_Tenant_After_Denied_Staged_Reparent_And_Commit(bool linq) {
        await using var store = await Store.CreateAsync(linq);
        var row = new Row { Name = "admitted" };
        await using (var scope = await store.EnterAsync(TenantA)) {
            await using var writer = scope.ServiceProvider.GetRequiredService<IRepository<Row>>();
            await using var transaction = writer.Begin();
            await writer.AddAsync(row);
            await using var updater = scope.ServiceProvider.GetRequiredService<IRepository<Row>>();
            updater.Join(transaction);
            row.TenantUid = null;
            if (!linq) ((IUnitOfWork<Database>)transaction).Context.ChangeTracker.DetectChanges();
            var original = await updater.GetOriginalAsync(row, value => value.TenantUid);
            Assert.True(original.Found);
            Assert.Equal(TenantA, original.Value);
            await Assert.ThrowsAsync<PermissionDeniedException>(() => updater.UpdateAsync(row));
            Assert.Equal(TenantA, row.TenantUid);
            await transaction.CommitAsync();
        }
        await using var reader = await store.EnterAsync(TenantA);
        await using var repository = reader.ServiceProvider.GetRequiredService<IRepository<Row>>();
        Assert.Equal(TenantA, (await repository.GetAsync(row))!.TenantUid);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Discard_Admission_When_Unit_Rolls_Back(bool linq) {
        await using var store = await Store.CreateAsync(linq);
        var row = new Row { Name = "rolled-back" };
        await using (var scope = await store.EnterAsync(TenantA)) {
            await using var writer = scope.ServiceProvider.GetRequiredService<IRepository<Row>>();
            await using var transaction = writer.Begin();
            await writer.AddAsync(row);
            await using var updater = scope.ServiceProvider.GetRequiredService<IRepository<Row>>();
            updater.Join(transaction);
            Assert.True((await updater.GetOriginalAsync(row, value => value.TenantUid)).Found);
            await transaction.RollbackAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await updater.GetOriginalAsync(row, value => value.TenantUid));
        }
        await using var reader = await store.EnterAsync(TenantA);
        await using var repository = reader.ServiceProvider.GetRequiredService<IRepository<Row>>();
        Assert.False((await repository.GetOriginalAsync(row, value => value.TenantUid)).Found);
        await Assert.ThrowsAsync<PermissionDeniedException>(() => repository.UpdateAsync(row));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Project_Persisted_Original_Without_Tracking_Or_Returning_Modified_Values(bool linq) {
        await using var store = await Store.CreateAsync(linq);
        var row = await store.AddAsync(TenantA, "persisted");
        await using var scope = await store.EnterAsync(TenantA);
        await using var repository = scope.ServiceProvider.GetRequiredService<IRepository<Row>>();
        await using var transaction = repository.Begin();
        var original = await repository.GetOriginalAsync(row, value => value.TenantUid);
        Assert.True(original.Found);
        Assert.Equal(TenantA, original.Value);
        if (!linq) Assert.Empty(((IUnitOfWork<Database>)transaction).Context.ChangeTracker.Entries());
        var loaded = (await repository.GetAsync(row))!;
        loaded.TenantUid = null;
        original = await repository.GetOriginalAsync(loaded, value => value.TenantUid);
        Assert.True(original.Found);
        Assert.Equal(TenantA, original.Value);
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task Reject_Original_Admission_Outside_Owning_Context_Or_With_Changed_Key() {
        await using var store = await Store.CreateAsync(false);
        await using var scope = await store.EnterAsync(TenantA);
        await using var writer = scope.ServiceProvider.GetRequiredService<IRepository<Row>>();
        await using var transaction = writer.Begin();
        var row = new Row { Name = "private" };
        await writer.AddAsync(row);
        await using var outsider = scope.ServiceProvider.GetRequiredService<IRepository<Row>>();
        Assert.False((await outsider.GetOriginalAsync(row, value => value.TenantUid)).Found);
        await using var joined = scope.ServiceProvider.GetRequiredService<IRepository<Row>>();
        joined.Join(transaction);
        var impostor = new Row { Uid = Guid.Parse("00000000-0000-0000-0000-000000000099"), TenantUid = TenantA };
        Assert.False((await joined.GetOriginalAsync(impostor, value => value.TenantUid)).Found);
        var originalKey = row.Uid;
        row.Uid = impostor.Uid;
        Assert.False((await joined.GetOriginalAsync(row, value => value.TenantUid)).Found);
        row.Uid = originalKey;
        await transaction.RollbackAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reject_Original_Entity_And_Method_Projections(bool linq) {
        await using var store = await Store.CreateAsync(linq);
        await using var scope = await store.EnterAsync(TenantA);
        await using var repository = scope.ServiceProvider.GetRequiredService<IRepository<Row>>();
        await using var transaction = repository.Begin();
        var row = new Row { Name = "private" };
        await repository.AddAsync(row);
        await Assert.ThrowsAsync<ArgumentException>(async () => await repository.GetOriginalAsync(row, value => value));
        await Assert.ThrowsAsync<ArgumentException>(async () => await repository.GetOriginalAsync(row, value => (object?)value.TenantUid));
        await Assert.ThrowsAsync<ArgumentException>(async () => await repository.GetOriginalAsync(row, value => value.Uid.ToString()));
        Assert.True((await repository.GetOriginalAsync(row, value => value.TenantUid)).Found);
        await transaction.RollbackAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Honor_Build_Query_Block_For_Original_Admission(bool linq) {
        var blocker = new Mock<IRepositoryBuildQueryAdvisor<Row>>();
        blocker.Setup(value => value.Order).Returns(int.MaxValue);
        blocker.Setup(value => value.AdviseAsync(It.IsAny<AdviceContext>(), It.IsAny<QueryContainer<Row>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AdviseResult.Block);
        await using var store = await Store.CreateAsync(linq, services => services.AddSingleton(blocker.Object));
        await using var scope = await store.EnterAsync(TenantA);
        await using var repository = scope.ServiceProvider.GetRequiredService<IRepository<Row>>();
        await using var transaction = repository.Begin();
        var row = new Row { Name = "blocked" };
        await repository.AddAsync(row);
        Assert.False((await repository.GetOriginalAsync(row, value => value.TenantUid)).Found);
        await Assert.ThrowsAsync<PermissionDeniedException>(() => repository.UpdateAsync(row));
        await transaction.RollbackAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Add_Then_Update_Then_Rollback_Keeps_No_New_Value(bool linq) {
        await using var store = await Store.CreateAsync(linq);
        var row = new Row { Name = "volatile", Value = "initial" };
        await using (var scope = await store.EnterAsync(TenantA)) {
            await using var repository = scope.ServiceProvider.GetRequiredService<IRepository<Row>>();
            await using var transaction = repository.Begin();
            Assert.Equal(MutationResult.Applied, await repository.AddAsync(row));
            row.Value = "changed";
            Assert.Equal(MutationResult.Applied, await repository.UpdateAsync(row));
            await transaction.RollbackAsync();
        }
        await using var reader = await store.EnterAsync(TenantA);
        await using var readRepository = reader.ServiceProvider.GetRequiredService<IRepository<Row>>();
        Assert.Null(await readRepository.FindAsync(new object[] { row.Uid }));
    }

    [System.ComponentModel.DataAnnotations.Schema.Table("Rows")]
    [PrimaryKey(nameof(Uid))]
    [CanonicalName("records/{record}")]
    public sealed class Row : ITenant, IIdentifier, ICanonicalName, IConcurrency
    {
        public Guid Uid { get; set; }
        [JsonIgnore]
        public Guid? TenantUid { get; set; }
        public string? Name { get; set; }
        public string? CanonicalName { get; set; }
        public string? Value { get; set; }
        [ConcurrencyCheck]
        public Guid Timestamp { get; set; }
    }

    public sealed class Database(DbContextOptions<Database> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder model) {
            model.Entity<Row>();
        }
    }

    public sealed class Connection(DataOptions options) : DataConnection(options);

    private sealed class Store : IAsyncDisposable
    {
        private readonly string                                             _path;
        private readonly Dictionary<Guid, SchemataTenant>                   _tenants;
        private readonly MemoryCacheTenantProviderCache                     _cache;
        private readonly ServiceProvider                                    _root;
        private readonly SchemataTenantServiceScopeFactory<SchemataTenant>  _scopes;

        private Store(
            string                                            path,
            Dictionary<Guid, SchemataTenant>                  tenants,
            MemoryCacheTenantProviderCache                    cache,
            ServiceProvider                                   root,
            SchemataTenantServiceScopeFactory<SchemataTenant> scopes
        ) {
            _path    = path;
            _tenants = tenants;
            _cache   = cache;
            _root    = root;
            _scopes  = scopes;
        }

        public static async Task<Store> CreateAsync(bool linq, Action<IServiceCollection>? configure = null) {
            var path = Path.Combine(Path.GetTempPath(), $"schemata-tenant-repository-{Guid.NewGuid():N}.db");
            var tenants = new Dictionary<Guid, SchemataTenant>();
            var manager = new Mock<ITenantManager<SchemataTenant>>();
            manager.Setup(value => value.FindByTenantId(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .Returns((Guid identifier, CancellationToken _) => new ValueTask<SchemataTenant?>(
                    tenants.TryGetValue(identifier, out var found) ? found : null));
            var services = new ServiceCollection();
            services.AddSingleton<ITenantManager<SchemataTenant>>(manager.Object);
            if (linq) {
                var mapping = new MappingSchema();
                mapping.AddMetadataReader(new SystemComponentModelDataAnnotationsSchemaAttributeReader());
                var options = new DataOptions().UseSQLite($"Data Source={path};Pooling=False").UseMappingSchema(mapping);
                services.AddSingleton<Func<Connection>>(_ => () => new(options));
                services.AddRepository<Row, LinqToDbRepository<Connection, Row>>();
                new SchemataTenancyBuilder<SchemataTenant>(services).UseRepositories(builder => {
                    builder.Services.AddTransient<IRepository<Row>, LinqToDbRepository<Connection, Row>>();
                    builder.Services.AddSingleton<Func<Connection>>(_ => () => new(options));
                });
            } else {
                services.AddDbContextFactory<Database>(options => options.UseSqlite($"Data Source={path};Pooling=False")
                    .ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
                services.AddRepository<Row, EfCoreRepository<Database, Row>>();
                new SchemataTenancyBuilder<SchemataTenant>(services).UseRepositories(builder => {
                    builder.Services.AddTransient<IRepository<Row>, EfCoreRepository<Database, Row>>();
                    builder.Services.AddDbContextFactory<Database>(options => options.UseSqlite($"Data Source={path};Pooling=False")
                        .ReplaceService<IModelCustomizer, SchemataModelCustomizer>());
                });
            }
            configure?.Invoke(services);
            var root = services.BuildServiceProvider();
            try {
                var tenancy = root.GetRequiredService<IOptions<SchemataTenancyOptions>>();
                var cache = new MemoryCacheTenantProviderCache(tenancy);
                var providers = new SchemataTenantServiceProviderFactory<SchemataTenant>(root, cache, tenancy);
                var store = new Store(path, tenants, cache, root, new(root, providers));
                if (linq) {
                    using var database = root.GetRequiredService<Func<Connection>>()();
                    database.CreateTable<Row>();
                } else {
                    await using var database = await root.GetRequiredService<IDbContextFactory<Database>>().CreateDbContextAsync();
                    await database.Database.EnsureCreatedAsync();
                }
                return store;
            } catch {
                await root.DisposeAsync();
                throw;
            }
        }

        public async Task<Row> AddAsync(Guid? tenant, string name) {
            await using var scope = await EnterAsync(tenant);
            await using var repository = scope.ServiceProvider.GetRequiredService<IRepository<Row>>();
            var row = new Row { Name = name };
            Assert.Equal(MutationResult.Applied, await repository.AddAsync(row));
            await repository.CommitAsync();
            return row;
        }

        private SchemataTenant Register(Guid uid) {
            if (_tenants.TryGetValue(uid, out var existing)) return existing;
            var tenant = new SchemataTenant { Uid = uid, Timestamp = Guid.NewGuid(), Name = uid.ToString() };
            _tenants[uid] = tenant;
            return tenant;
        }

        // The fixture catalog resolves every requested identifier as an existing tenant.
        public ValueTask<AsyncServiceScope> EnterAsync(Guid? tenant) {
            if (tenant is { } uid) Register(uid);
            return _scopes.CreateAsync(new(tenant));
        }

        public async ValueTask DisposeAsync() {
            await _cache.DisposeAsync();
            await _root.DisposeAsync();
            File.Delete(_path);
        }
    }
}
