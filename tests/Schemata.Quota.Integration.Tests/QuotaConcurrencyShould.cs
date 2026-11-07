using System;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.Repository;
using Schemata.Quota.Integration.Tests.Fixtures;
using Schemata.Quota.Skeleton;
using Xunit;

namespace Schemata.Quota.Integration.Tests;

[Trait("Category", "Integration")]
public class QuotaConcurrencyShould
{
    public static TheoryData<QuotaProvider> Providers => new() { QuotaProvider.EntityFrameworkCore, QuotaProvider.LinqToDb };

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Arbitrate_Concurrent_First_Inserts_For_One_Operation(QuotaProvider provider) {
        await using var fixture = new QuotaProviderFixture(provider);
        await fixture.InitializeAsync();
        var request = QuotaProviderFixture.Request("same-operation", amount: 3);

        var results = await Task.WhenAll(Capture(() => fixture.ReserveAsync(request)), Capture(() => fixture.ReserveAsync(request)));

        Assert.InRange(results.Count(result => result.Error is null), 1, 2);
        Assert.All(results, result => Assert.True(result.Error is null or AlreadyExistsException or AbortedException));
        var replay = await fixture.ReserveAsync(request);
        var accounts = await fixture.AccountsAsync();
        var reservations = await fixture.ReservationsAsync();
        Assert.Equal(results.First(result => result.Error is null).Value!.Uid, replay.Uid);
        Assert.Single(results.Where(result => result.Error is null).Select(result => result.Value!.Uid).Distinct());
        Assert.Equal(3, Assert.Single(accounts).Reserved);
        Assert.Single(reservations);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Never_Exceed_Capacity_When_New_Account_Writes_Race(QuotaProvider provider) {
        await using var fixture = new QuotaProviderFixture(provider);
        await fixture.InitializeAsync();

        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(index =>
            Capture(() => fixture.ReserveAsync(QuotaProviderFixture.Request($"race-{index}", amount: 2, limit: 5)))));

        Assert.All(results, result => Assert.True(result.Error is null or QuotaExceededException or AlreadyExistsException or AbortedException));
        var account = Assert.Single(await fixture.AccountsAsync());
        Assert.InRange(account.Reserved, 0, 5);
        Assert.Equal(account.Reserved, (await fixture.ReservationsAsync()).Sum(reservation => reservation.Items.Sum(item => item.Amount)));
        Assert.Equal(results.Count(result => result.Error is null) * 2, account.Reserved);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Replay_Operation_Id_Without_Double_Debit_Across_Fresh_Scopes(QuotaProvider provider) {
        await using var fixture = new QuotaProviderFixture(provider);
        await fixture.InitializeAsync();
        var request = QuotaProviderFixture.Request("idempotent", amount: 4);

        var first = await fixture.ReserveAsync(request);
        var second = await fixture.ReserveAsync(request);

        Assert.Equal(first.Uid, second.Uid);
        Assert.Equal(4, Assert.Single(await fixture.AccountsAsync()).Reserved);
        Assert.Single(await fixture.ReservationsAsync());
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Reject_Mismatched_Idempotency_Fingerprint_Without_Writes(QuotaProvider provider) {
        await using var fixture = new QuotaProviderFixture(provider);
        await fixture.InitializeAsync();
        var original = await fixture.ReserveAsync(QuotaProviderFixture.Request("fingerprint", amount: 2));

        await Assert.ThrowsAsync<AlreadyExistsException>(() =>
            fixture.ReserveAsync(QuotaProviderFixture.Request("fingerprint", amount: 3)).AsTask());

        var account = Assert.Single(await fixture.AccountsAsync());
        var reservation = Assert.Single(await fixture.ReservationsAsync());
        Assert.Equal(2, account.Reserved);
        Assert.Equal(original.Uid, reservation.Uid);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Roll_Back_Every_Metric_When_One_Charge_Overflows(QuotaProvider provider) {
        await using var fixture = new QuotaProviderFixture(provider);
        await fixture.InitializeAsync();
        var request = new QuotaReservationRequest(null, new("subject"), "atomic", [
            new(new("requests", 1, QuotaConstants.Algorithms.FixedWindow, 10, TimeSpan.FromMinutes(1), 10), new("calls"), 4),
            new(new("requests", 1, QuotaConstants.Algorithms.FixedWindow, 5, TimeSpan.FromMinutes(1), 5), new("tokens"), 6),
        ], null);

        var error = await Assert.ThrowsAsync<QuotaExceededException>(() => fixture.ReserveAsync(request).AsTask());

        var violation = Assert.Single(error.Details!.OfType<Schemata.Abstractions.Errors.QuotaFailureDetail>().Single().Violations!);
        Assert.Equal("tokens", violation.QuotaMetric);
        Assert.Empty(await fixture.AccountsAsync());
        Assert.Empty(await fixture.ReservationsAsync());
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Detect_Stale_Account_Cas_With_Default_Outer_Transaction_Isolation(QuotaProvider provider) {
        await using var fixture = new QuotaProviderFixture(provider);
        await fixture.InitializeAsync();
        await fixture.ReserveAsync(QuotaProviderFixture.Request("seed", amount: 1));

        var firstScope = fixture.CreateScope();
        var secondScope = fixture.CreateScope();
        using (firstScope)
        using (secondScope) {
            var firstStore = firstScope.ServiceProvider.GetRequiredService<IQuotaStore>();
            var secondStore = secondScope.ServiceProvider.GetRequiredService<IQuotaStore>();
            var firstAccount = await firstStore.FindAccountAsync(new("scope"), new("subject"), new("calls"), "requests", 1, default);
            var secondAccount = await secondStore.FindAccountAsync(new("scope"), new("subject"), new("calls"), "requests", 1, default);
            Assert.NotNull(firstAccount);
            Assert.NotNull(secondAccount);
            Assert.Equal(firstAccount.Timestamp, secondAccount.Timestamp);

            var first = firstStore.Begin(IsolationLevel.Unspecified);
            await using (first) {
                firstAccount.Reserved += 2;
                await firstStore.SaveAccountAsync(firstAccount, default);
                await first.CommitAsync();
            }

            var second = secondStore.Begin(IsolationLevel.Unspecified);
            await using (second) {
                secondAccount.Reserved += 2;
                if (provider == QuotaProvider.LinqToDb) {
                    await Assert.ThrowsAsync<AbortedException>(() => secondStore.SaveAccountAsync(secondAccount, default));
                } else {
                    await secondStore.SaveAccountAsync(secondAccount, default);
                    await Assert.ThrowsAsync<AbortedException>(() => second.CommitAsync());
                }
            }
        }

        Assert.Equal(3, Assert.Single(await fixture.AccountsAsync()).Reserved);
        Assert.Single(await fixture.ReservationsAsync());
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Commit_Enlisted_Reserve_And_Settlement_Only_With_The_Caller(QuotaProvider provider) {
        await using var fixture = new QuotaProviderFixture(provider);
        await fixture.InitializeAsync();
        var existing = await fixture.ReserveAsync(QuotaProviderFixture.Request("settle-first", amount: 1));
        using var scope = fixture.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IQuotaStore>();
        var accounting = scope.ServiceProvider.GetRequiredService<IQuotaAccounting>();
        var transaction = store.Begin(IsolationLevel.Unspecified);
        await using (transaction) {
            var settled = await accounting.CommitAsync(new(null, new("subject"), existing.Uid), transaction);
            var reserved = await accounting.ReserveAsync(QuotaProviderFixture.Request("reserve-second", amount: 2), transaction);
            Assert.Equal(QuotaReservationState.Committed, settled.State);
            Assert.Equal(QuotaReservationState.Pending, reserved.State);
            await transaction.CommitAsync();
        }

        var account = Assert.Single(await fixture.AccountsAsync());
        Assert.Equal(1, account.Committed);
        Assert.Equal(2, account.Reserved);
        Assert.Equal(QuotaReservationState.Committed, (await fixture.ReservationAsync("settle-first"))!.State);
        Assert.Equal(QuotaReservationState.Pending, (await fixture.ReservationAsync("reserve-second"))!.State);
        Assert.Equal(QuotaSettlementKind.Commit, Assert.Single(await fixture.LedgerAsync()).Kind);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Roll_Back_Every_Enlisted_Charge_When_One_Metric_Is_Rejected(QuotaProvider provider) {
        await using var fixture = new QuotaProviderFixture(provider);
        await fixture.InitializeAsync();
        using var scope = fixture.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IQuotaStore>();
        var accounting = scope.ServiceProvider.GetRequiredService<IQuotaAccounting>();
        var request = new QuotaReservationRequest(null, new("subject"), "enlisted-atomic", [
            new(new("requests", 1, QuotaConstants.Algorithms.FixedWindow, 10, TimeSpan.FromMinutes(1), 10), new("calls"), 4),
            new(new("requests", 1, QuotaConstants.Algorithms.FixedWindow, 5, TimeSpan.FromMinutes(1), 5), new("tokens"), 6),
        ], null);
        var transaction = store.Begin(IsolationLevel.Unspecified);
        await using (transaction) {
            await Assert.ThrowsAsync<QuotaExceededException>(() => accounting.ReserveAsync(request, transaction).AsTask());
            await Assert.ThrowsAnyAsync<InvalidOperationException>(() => transaction.CommitAsync());
        }

        Assert.Empty(await fixture.AccountsAsync());
        Assert.Empty(await fixture.ReservationsAsync());
        Assert.Empty(await fixture.LedgerAsync());
    }

    [Theory]
    [InlineData(QuotaProvider.EntityFrameworkCore, QuotaSettlementKind.Commit)]
    [InlineData(QuotaProvider.EntityFrameworkCore, QuotaSettlementKind.Release)]
    [InlineData(QuotaProvider.LinqToDb, QuotaSettlementKind.Commit)]
    [InlineData(QuotaProvider.LinqToDb, QuotaSettlementKind.Release)]
    public async Task Reject_Wrong_Identity_Before_Enlisted_Settlement_Writes(QuotaProvider provider, QuotaSettlementKind kind) {
        await using var fixture = new QuotaProviderFixture(provider);
        await fixture.InitializeAsync();
        var reserved = await fixture.ReserveAsync(QuotaProviderFixture.Request("identity", amount: 2));
        using var scope = fixture.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IQuotaStore>();
        var accounting = scope.ServiceProvider.GetRequiredService<IQuotaAccounting>();
        var transaction = store.Begin(IsolationLevel.Unspecified);
        await using (transaction) {
            var request = new QuotaSettlementRequest(new("wrong"), new("subject"), reserved.Uid);
            await Assert.ThrowsAsync<NotFoundException>(() => (kind == QuotaSettlementKind.Commit
                ? accounting.CommitAsync(request, transaction)
                : accounting.ReleaseAsync(request, transaction)).AsTask());
            await transaction.CommitAsync();
        }

        var account = Assert.Single(await fixture.AccountsAsync());
        Assert.Equal(0, account.Committed);
        Assert.Equal(2, account.Reserved);
        Assert.Equal(QuotaReservationState.Pending, (await fixture.ReservationAsync(reserved.Uid))!.State);
        Assert.Empty(await fixture.LedgerAsync());
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Reject_Wrong_Subject_Before_Enlisted_Settlement_Writes(QuotaProvider provider) {
        await using var fixture = new QuotaProviderFixture(provider);
        await fixture.InitializeAsync();
        var reserved = await fixture.ReserveAsync(QuotaProviderFixture.Request("subject-identity", amount: 2));
        using var scope = fixture.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IQuotaStore>();
        var accounting = scope.ServiceProvider.GetRequiredService<IQuotaAccounting>();
        var transaction = store.Begin(IsolationLevel.Unspecified);
        await using (transaction) {
            await Assert.ThrowsAsync<NotFoundException>(() =>
                accounting.CommitAsync(new(null, new("wrong"), reserved.Uid), transaction).AsTask());
            await transaction.CommitAsync();
        }

        var account = Assert.Single(await fixture.AccountsAsync());
        Assert.Equal(0, account.Committed);
        Assert.Equal(2, account.Reserved);
        Assert.Equal(QuotaReservationState.Pending, (await fixture.ReservationAsync(reserved.Uid))!.State);
        Assert.Empty(await fixture.LedgerAsync());
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Surface_Enlisted_Settlement_Cas_Abort_After_Caller_Rollback(QuotaProvider provider) {
        await using var fixture = new QuotaProviderFixture(provider, contended: true);
        await fixture.InitializeAsync();
        var reserved = await fixture.ReserveAsync(QuotaProviderFixture.Request("contended", amount: 2));
        using var loserScope = fixture.CreateScope();
        var loserStore = loserScope.ServiceProvider.GetRequiredService<IQuotaStore>();
        var stale = await loserStore.GetReservationAsync(reserved.Uid, default);
        Assert.NotNull(stale);
        using (var winnerScope = fixture.CreateScope()) {
            await winnerScope.ServiceProvider.GetRequiredService<IQuotaAccounting>()
                             .CommitAsync(new(null, new("subject"), reserved.Uid));
        }

        fixture.Contention.Arm(stale);
        var transaction = loserStore.Begin(IsolationLevel.Unspecified);
        await using (transaction) {
            var accounting = loserScope.ServiceProvider.GetRequiredService<IQuotaAccounting>();
            await Assert.ThrowsAsync<AbortedException>(() =>
                accounting.ReleaseAsync(new(null, new("subject"), reserved.Uid), transaction).AsTask());
            await Assert.ThrowsAnyAsync<InvalidOperationException>(() => transaction.CommitAsync());
        }

        Assert.Equal(QuotaReservationState.Committed, (await fixture.ReservationAsync(reserved.Uid))!.State);
        Assert.Equal(QuotaSettlementKind.Commit, Assert.Single(await fixture.LedgerAsync()).Kind);
        var account = Assert.Single(await fixture.AccountsAsync());
        Assert.Equal(2, account.Committed);
        Assert.Equal(0, account.Reserved);
    }

    private static async Task<(QuotaReservationView? Value, Exception? Error)> Capture(Func<ValueTask<QuotaReservationView>> operation) {
        try {
            return (await operation(), null);
        } catch (Exception error) {
            return (null, error);
        }
    }
}
