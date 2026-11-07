using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Quota.Integration.Tests.Fixtures;
using Schemata.Quota.Skeleton;
using Xunit;

namespace Schemata.Quota.Integration.Tests;

[Trait("Category", "Integration")]
public class QuotaExpiryShould
{
    public static TheoryData<QuotaProvider> Providers => new() { QuotaProvider.EntityFrameworkCore, QuotaProvider.LinqToDb };

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Settle_A_Reservation_Exactly_Once_When_Finalizers_Race(QuotaProvider provider) {
        await using var fixture = new QuotaProviderFixture(provider);
        await fixture.InitializeAsync();
        var reserved = await fixture.ReserveAsync(QuotaProviderFixture.Request("race", amount: 2));

        var results = await Task.WhenAll(
            Capture(() => SettleAsync(fixture, reserved.Uid, QuotaSettlementKind.Commit)),
            Capture(() => SettleAsync(fixture, reserved.Uid, QuotaSettlementKind.Release)),
            Capture(() => SettleAsync(fixture, reserved.Uid, QuotaSettlementKind.Expire)));

        Assert.All(results, result => Assert.Null(result));
        var reservation = await fixture.ReservationAsync(reserved.Uid);
        Assert.NotNull(reservation);
        Assert.Contains(reservation.State, new[] { QuotaReservationState.Committed, QuotaReservationState.Released, QuotaReservationState.Expired });
        var ledger = Assert.Single(await fixture.LedgerAsync());
        Assert.Equal(reservation.Uid, ledger.ReservationUid);
        var account = Assert.Single(await fixture.AccountsAsync());
        if (reservation.State == QuotaReservationState.Committed) {
            Assert.Equal(2, account.Committed);
            Assert.Equal(0, account.Reserved);
        } else {
            Assert.Equal(0, account.Committed);
            Assert.Equal(0, account.Reserved);
        }
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Recover_Expired_Concurrency_Leases_Lazily_Without_A_Scheduler(QuotaProvider provider) {
        await using var fixture = new QuotaProviderFixture(provider);
        await fixture.InitializeAsync();
        var expires = fixture.Clock.GetUtcNow().AddMinutes(1);
        var first = await fixture.ReserveAsync(QuotaProviderFixture.Request("abandoned", limit: 1,
            algorithm: QuotaConstants.Algorithms.Concurrency, expiresAt: expires));

        fixture.Clock.Advance(TimeSpan.FromMinutes(2));
        var second = await fixture.ReserveAsync(QuotaProviderFixture.Request("replacement", limit: 1,
            algorithm: QuotaConstants.Algorithms.Concurrency, expiresAt: fixture.Clock.GetUtcNow().AddMinutes(1)));

        Assert.Equal(QuotaReservationState.Expired, (await fixture.ReservationAsync(first.Uid))!.State);
        Assert.Equal(QuotaReservationState.Pending, second.State);
        var account = Assert.Single(await fixture.AccountsAsync());
        Assert.Equal(0, account.Committed);
        Assert.Equal(1, account.Reserved);
        Assert.Equal(QuotaSettlementKind.Expire, Assert.Single(await fixture.LedgerAsync()).Kind);
    }

    [Fact]
    public async Task Recover_Expiry_Through_A_Real_Scheduler_Trigger() {
        await using var fixture = new QuotaProviderFixture(QuotaProvider.EntityFrameworkCore, scheduling: true);
        await fixture.InitializeAsync();
        var reserved = await fixture.ReserveAsync(QuotaProviderFixture.Request("scheduled", limit: 1,
            algorithm: QuotaConstants.Algorithms.Concurrency, expiresAt: fixture.Clock.GetUtcNow().AddMilliseconds(250)));
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));

        var deadline = DateTime.UtcNow.AddSeconds(15);
        Schemata.Quota.Skeleton.Entities.SchemataQuotaReservation? reservation;
        do {
            await Task.Delay(25);
            reservation = await fixture.ReservationAsync(reserved.Uid);
        } while (reservation?.State == QuotaReservationState.Pending && DateTime.UtcNow < deadline);

        Assert.Equal(QuotaReservationState.Expired, reservation?.State);
        Assert.Equal(0, Assert.Single(await fixture.AccountsAsync()).Reserved);
        Assert.Equal(QuotaSettlementKind.Expire, Assert.Single(await fixture.LedgerAsync()).Kind);
    }

    [Theory]
    [InlineData(QuotaProvider.EntityFrameworkCore, QuotaConstants.Algorithms.FixedWindow, QuotaSettlementKind.Commit)]
    [InlineData(QuotaProvider.LinqToDb, QuotaConstants.Algorithms.FixedWindow, QuotaSettlementKind.Commit)]
    [InlineData(QuotaProvider.EntityFrameworkCore, QuotaConstants.Algorithms.FixedWindow, QuotaSettlementKind.Release)]
    [InlineData(QuotaProvider.LinqToDb, QuotaConstants.Algorithms.FixedWindow, QuotaSettlementKind.Release)]
    [InlineData(QuotaProvider.EntityFrameworkCore, QuotaConstants.Algorithms.FixedWindow, QuotaSettlementKind.Expire)]
    [InlineData(QuotaProvider.LinqToDb, QuotaConstants.Algorithms.FixedWindow, QuotaSettlementKind.Expire)]
    [InlineData(QuotaProvider.EntityFrameworkCore, QuotaConstants.Algorithms.SlidingWindow, QuotaSettlementKind.Commit)]
    [InlineData(QuotaProvider.LinqToDb, QuotaConstants.Algorithms.SlidingWindow, QuotaSettlementKind.Commit)]
    [InlineData(QuotaProvider.EntityFrameworkCore, QuotaConstants.Algorithms.SlidingWindow, QuotaSettlementKind.Release)]
    [InlineData(QuotaProvider.LinqToDb, QuotaConstants.Algorithms.SlidingWindow, QuotaSettlementKind.Release)]
    [InlineData(QuotaProvider.EntityFrameworkCore, QuotaConstants.Algorithms.SlidingWindow, QuotaSettlementKind.Expire)]
    [InlineData(QuotaProvider.LinqToDb, QuotaConstants.Algorithms.SlidingWindow, QuotaSettlementKind.Expire)]
    public async Task Late_Settlement_Preserves_A_Newer_Reservation(QuotaProvider provider, string algorithm, QuotaSettlementKind kind) {
        await using var fixture = new QuotaProviderFixture(provider);
        await fixture.InitializeAsync();
        var first = await fixture.ReserveAsync(QuotaProviderFixture.Request("old", amount: 5, algorithm: algorithm));
        fixture.Clock.Advance(TimeSpan.FromSeconds(61));
        var second = await fixture.ReserveAsync(QuotaProviderFixture.Request("new", amount: 5, algorithm: algorithm));

        await SettleAsync(fixture, first.Uid, kind);

        var account = Assert.Single(await fixture.AccountsAsync());
        Assert.Equal(5, account.Reserved);
        Assert.Equal(0, account.Committed);
        Assert.Equal(QuotaReservationState.Pending, (await fixture.ReservationAsync(second.Uid))?.State);
        await Assert.ThrowsAsync<Schemata.Abstractions.Exceptions.QuotaExceededException>(async () =>
            await fixture.ReserveAsync(QuotaProviderFixture.Request("excess", amount: 6, algorithm: algorithm)));
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Out_Of_Order_Sliding_Release_Does_Not_Leave_Released_Usage(QuotaProvider provider) {
        await using var fixture = new QuotaProviderFixture(provider);
        await fixture.InitializeAsync();
        await fixture.ReserveAsync(QuotaProviderFixture.Request("first", amount: 5, algorithm: QuotaConstants.Algorithms.SlidingWindow));
        fixture.Clock.Advance(TimeSpan.FromSeconds(30));
        var second = await fixture.ReserveAsync(QuotaProviderFixture.Request("second", amount: 5, algorithm: QuotaConstants.Algorithms.SlidingWindow));
        await SettleAsync(fixture, second.Uid, QuotaSettlementKind.Release);
        fixture.Clock.Advance(TimeSpan.FromSeconds(31));

        var third = await fixture.ReserveAsync(QuotaProviderFixture.Request("third", amount: 10, algorithm: QuotaConstants.Algorithms.SlidingWindow));

        Assert.Equal(QuotaReservationState.Pending, third.State);
        Assert.Equal(10, Assert.Single(await fixture.AccountsAsync()).Reserved);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Ledger_Metric_Filter_Excludes_Other_Items_Before_Paging(QuotaProvider provider) {
        await using var fixture = new QuotaProviderFixture(provider);
        await fixture.InitializeAsync();
        var policy = new QuotaPolicy("requests", 1, QuotaConstants.Algorithms.FixedWindow, 10, TimeSpan.FromMinutes(1), 10);
        var reservation = await fixture.ReserveAsync(new(null, new("subject"), "multi", [
            new(policy, new("calls"), 2), new(policy, new("tokens"), 3),
        ], null));
        await SettleAsync(fixture, reservation.Uid, QuotaSettlementKind.Commit);
        using var scope = fixture.CreateScope();
        var accounting = scope.ServiceProvider.GetRequiredService<IQuotaAccounting>();
        foreach (var (metric, amount) in new[] { ("calls", 2L), ("tokens", 3L) }) {
            var entries = await accounting.ListLedgerAsync(new(new("scope"), new("subject"), new QuotaMetric(metric), 1));
            var entry = Assert.Single(entries);
            Assert.Equal(amount, entry.Amount);
            Assert.Equal(reservation.Uid, entry.ReservationUid);
        }
    }

    private static async ValueTask<QuotaReservationView> SettleAsync(
        QuotaProviderFixture fixture,
        Guid uid,
        QuotaSettlementKind kind
    ) {
        using var scope = fixture.CreateScope();
        var accounting = scope.ServiceProvider.GetRequiredService<IQuotaAccounting>();
        return kind switch {
            QuotaSettlementKind.Commit => await accounting.CommitAsync(new(null, new("subject"), uid)),
            QuotaSettlementKind.Release => await accounting.ReleaseAsync(new(null, new("subject"), uid)),
            _ => await accounting.ExpireAsync(uid),
        };
    }

    private static async Task<Exception?> Capture(Func<ValueTask<QuotaReservationView>> operation) {
        try {
            await operation();
            return null;
        } catch (Exception error) {
            return error;
        }
    }
}
