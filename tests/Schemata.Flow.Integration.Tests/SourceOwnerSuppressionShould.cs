using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Exceptions;
using Schemata.Common;
using Schemata.Entity.Repository;
using Schemata.Entity.Repository.Advisors;
using Schemata.Flow.Foundation;
using Schemata.Flow.Integration.Tests.Fixtures;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Xunit;

namespace Schemata.Flow.Integration.Tests;

[Trait("Category", "Integration")]
public sealed class SourceOwnerSuppressionShould : IClassFixture<OwnedSourceFixture>
{
    private const string OwnerA = "users/a";
    private const string OwnerB = "users/b";

    private readonly OwnedSourceFixture _fixture;

    public SourceOwnerSuppressionShould(OwnedSourceFixture fixture) { _fixture = fixture; }

    [Fact]
    public async Task SourceOwner_Timer_Continuation_Writes_Back_Without_Ambient_Owner() {
        var order   = await CreateOrderAsync(OwnerA);
        var process = await StartAsync(nameof(OwnedTimerProcess), order);
        var token   = await WaitingTokenAsync(process.Name!);

        AmbientOwner.Current.Value = null;
        await RunTimerAsync(process.CanonicalName!, token.CanonicalName);

        var persisted = await ReadAsync(order.Uid);
        Assert.Equal("apply", persisted.State);
        Assert.Equal(OwnerA, persisted.Owner);
    }

    [Fact]
    public async Task SourceOwner_Task_Body_Loads_Source_Without_Ambient_Owner() {
        var order   = await CreateOrderAsync(OwnerA);
        var process = await StartAsync(nameof(OwnedTaskProcess), order);

        AmbientOwner.Current.Value = null;
        using var scope  = _fixture.CreateScope();
        var       runner = scope.ServiceProvider.GetRequiredService<FlowRunner>();
        await runner.CompleteAsync(process, null, null, default);

        var persisted = await ReadAsync(order.Uid);
        Assert.Equal("touched", persisted.TaskValue);
    }

    [Fact]
    public async Task SourceOwner_Start_Shaped_Read_Stays_Owner_Filtered() {
        var order = await CreateOrderAsync(OwnerA);

        AmbientOwner.Current.Value = OwnerB;
        using var scope      = _fixture.CreateScope();
        var       repository = scope.ServiceProvider.GetRequiredService<IRepository<OwnedOrder>>();

        OwnedOrder? loaded;
        using (repository.SuppressQuerySoftDelete()) {
            loaded = await repository.SingleOrDefaultAsync(
                q => q.Where(e => e.CanonicalName == order.CanonicalName), default);
        }

        Assert.Null(loaded);
    }

    [Fact]
    public async Task SourceOwner_SoftDeleted_Source_Is_Still_Written_Back() {
        var order   = await CreateOrderAsync(OwnerA, deleted: true);
        var process = await StartAsync(nameof(OwnedTimerProcess), order);
        var token   = await WaitingTokenAsync(process.Name!);

        AmbientOwner.Current.Value = null;
        await RunTimerAsync(process.CanonicalName!, token.CanonicalName);

        var persisted = await ReadAsync(order.Uid);
        Assert.Equal("apply", persisted.State);
        Assert.NotNull(persisted.DeleteTime);
    }

    [Fact]
    public async Task SourceOwner_Filter_Applies_Again_After_The_Flow_Scope() {
        var order   = await CreateOrderAsync(OwnerA);
        var process = await StartAsync(nameof(OwnedTimerProcess), order);
        var token   = await WaitingTokenAsync(process.Name!);

        AmbientOwner.Current.Value = null;
        await RunTimerAsync(process.CanonicalName!, token.CanonicalName);

        using var scope      = _fixture.CreateScope();
        var       repository = scope.ServiceProvider.GetRequiredService<IRepository<OwnedOrder>>();
        await Assert.ThrowsAsync<PermissionDeniedException>(async () =>
            await repository.SingleOrDefaultAsync(q => q.Where(e => e.CanonicalName == order.CanonicalName), default));
    }

    private async Task<OwnedOrder> CreateOrderAsync(string owner, bool deleted = false) {
        AmbientOwner.Current.Value = owner;
        var leaf = Identifiers.NewUid().ToString("n");
        var order = new OwnedOrder {
            Name          = leaf,
            CanonicalName = $"ownedOrders/{leaf}",
            State         = "created",
            Owner         = owner,
        };

        using var scope      = _fixture.CreateScope();
        var       repository = scope.ServiceProvider.GetRequiredService<IRepository<OwnedOrder>>();
        await repository.AddAsync(order, default);
        await repository.CommitAsync(default);

        if (deleted) {
            var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            var row = await db.OwnedOrders.SingleAsync(e => e.Uid == order.Uid);
            row.DeleteTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            await db.SaveChangesAsync();
            order.DeleteTime = row.DeleteTime;
        }

        return order;
    }

    private async Task<SchemataProcess> StartAsync(string definitionName, OwnedOrder order) {
        using var scope  = _fixture.CreateScope();
        var       runner = scope.ServiceProvider.GetRequiredService<FlowRunner>();
        return await runner.StartAsync(definitionName, order);
    }

    private async Task<SchemataProcessToken> WaitingTokenAsync(string processName) {
        using var scope      = _fixture.CreateScope();
        var       repository = scope.ServiceProvider.GetRequiredService<IRepository<SchemataProcessToken>>();
        var tokens = new List<SchemataProcessToken>();
        await foreach (var token in repository.ListAsync<SchemataProcessToken>(
                           q => q.Where(t => t.Process == processName && t.State == "Waiting"), default)) {
            tokens.Add(token);
        }

        return Assert.Single(tokens);
    }

    private async Task RunTimerAsync(string processCanonicalName, string? tokenName) {
        using var scope  = _fixture.CreateScope();
        var       runner = scope.ServiceProvider.GetRequiredService<FlowRunner>();
        var timer = new TimerDefinition {
            Name           = "owned-timer",
            TimerType      = TimerType.Duration,
            TimeExpression = XmlConvert.ToString(TimeSpan.FromHours(1)),
        };
        await runner.RunEventAsync(processCanonicalName, tokenName, timer, null, default);
    }

    private async Task<OwnedOrder> ReadAsync(Guid uid) {
        AmbientOwner.Current.Value = OwnerA;
        using var scope      = _fixture.CreateScope();
        var       repository = scope.ServiceProvider.GetRequiredService<IRepository<OwnedOrder>>();
        OwnedOrder? persisted;
        using (repository.SuppressQuerySoftDelete()) {
            persisted = await repository.SingleOrDefaultAsync(q => q.Where(e => e.Uid == uid), default);
        }

        Assert.NotNull(persisted);
        return persisted!;
    }
}
