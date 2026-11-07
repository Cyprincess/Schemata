using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;
using Schemata.Messaging.Skeleton;
using Schemata.Push.Actor.Tests.Fixtures;
using Schemata.Push.Foundation.Commands;
using Schemata.Abstractions.Exceptions;
using Schemata.Push.Skeleton;
using Schemata.Push.Skeleton.Entities;
using Schemata.Push.Skeleton.Control;
using Schemata.Push.Skeleton.Models;
using Xunit;

using Schemata.Abstractions.Advisors;
namespace Schemata.Push.Actor.Tests;

public class PushActorConcurrencyShould
{
    private const int Concurrency = 24;

    [Fact]
    public async Task Concurrent_Adds_To_Same_Triple_Create_Exactly_One_Row_When_Actor_Bridge_Is_Installed() {
        await using var harness = await PushActorConcurrencyHarness.BuildAsync(withActor: true);
        using var scope = harness.Root.CreateScope();

        var handler = scope.ServiceProvider.GetRequiredService<
            IRequestHandler<CreatePushControlRequest, PushSubscriptionInfo?>>();

        var tasks = Enumerable.Range(0, Concurrency)
                              .Select(_ => Task.Run(() => handler.HandleAsync(
                                                         new(Caller(), "email", "primary"), new AdviceContext(scope.ServiceProvider),
                                                         CancellationToken.None)))
                              .ToArray();
        var results = (await Task.WhenAll(tasks)).Select(result => Assert.IsType<PushSubscriptionInfo>(result)).ToArray();

        Assert.Single(results.Select(result => result.Uid).Distinct());
        var subscriptions = await SubscriptionRows(harness);
        var persisted = Assert.Single(subscriptions);
        Assert.All(results, result => {
            Assert.Equal(persisted.Uid, result.Uid);
            Assert.Equal(persisted.CanonicalName, result.CanonicalName);
            Assert.Equal(persisted.Provider, result.Provider);
        });
    }


    [Fact]
    public async Task Add_WhenAnotherSerializableAddHoldsTheWriteLock_SurfacesAborted() {
        var gate = new FirstAddCommitGate();
        await using var harness = await PushActorConcurrencyHarness.BuildAsync(withActor: false, addAdvisor: gate);

        await using var parkedScope = harness.Root.CreateAsyncScope();
        var parked = Task.Run(() => parkedScope.ServiceProvider.GetRequiredService<
                                   IRequestHandler<AddPushSubscriptionRequest, SchemataPushSubscription?>>()
                              .HandleAsync(new("owners/one", "email", "primary"), new AdviceContext(parkedScope.ServiceProvider), CancellationToken.None));

        // The parked add's uniqueness lookup has passed and its transaction holds the write lock.
        await gate.Holding.WaitAsync(TimeSpan.FromSeconds(30));

        await using var racingScope = harness.Root.CreateAsyncScope();
        try {
            await Assert.ThrowsAsync<AbortedException>(() => racingScope.ServiceProvider.GetRequiredService<
                IRequestHandler<AddPushSubscriptionRequest, SchemataPushSubscription?>>()
                .HandleAsync(new("owners/one", "email", "primary"), new AdviceContext(racingScope.ServiceProvider), CancellationToken.None));
        } finally {
            gate.Release();
        }

        var committed = await parked;
        Assert.NotNull(committed);
        Assert.Equal("owners/one", committed.Owner);
        var row = Assert.Single(await SubscriptionRows(harness));
        Assert.Equal(committed.Uid, row.Uid);
    }

    [Fact]
    public async Task Concurrent_Removes_Of_The_Same_Subscription_Are_Serialized_By_The_Actor() {
        await using var harness = await PushActorConcurrencyHarness.BuildAsync(withActor: true);

        {
            using var seed = harness.Root.CreateScope();
            await seed.ServiceProvider.GetRequiredService<IRequestDispatcher>()
                      .SendAsync<CreatePushControlRequest, PushSubscriptionInfo?>(
                          new(Caller(), "email", "primary"), CancellationToken.None);
        }

        using var scope   = harness.Root.CreateScope();
        var       handler = scope.ServiceProvider.GetRequiredService<
            IRequestHandler<DeletePushControlRequest, Unit>>();
        var tasks = Enumerable.Range(0, Concurrency)
                              .Select(_ => Task.Run(() => handler.HandleAsync(
                                                         new(Caller(), "email", "primary"), new AdviceContext(scope.ServiceProvider),
                                                         CancellationToken.None)))
                              .ToArray();
        await Task.WhenAll(tasks);

        var rows = await SubscriptionRows(harness);
        Assert.Single(rows);
        Assert.NotNull(rows[0].DeleteTime);
    }

    private static ClaimsPrincipal Caller() => new(new ClaimsIdentity([
        new Claim("sub", "owners/one"),
        new Claim("permission", PushPolicies.Create),
        new Claim("permission", PushPolicies.List),
        new Claim("permission", PushPolicies.Delete),
    ], "Test"));

    private static async Task<List<SchemataPushSubscription>> SubscriptionRows(
        PushActorConcurrencyHarness harness
    ) {
        await using var scope   = harness.Root.CreateAsyncScope();
        var             factory = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        return await factory.Set<SchemataPushSubscription>().ToListAsync();
    }
}