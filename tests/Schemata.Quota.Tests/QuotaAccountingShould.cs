using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.Repository;
using Schemata.Quota.Foundation.Runtime;
using Schemata.Quota.Skeleton;
using Schemata.Quota.Skeleton.Entities;
using Xunit;

namespace Schemata.Quota.Tests;

public class QuotaAccountingShould
{
    [Fact]
    public async Task Reject_Missing_Scope_Before_Beginning_A_Transaction() {
        var store = new Mock<IQuotaStore>(MockBehavior.Strict);
        await using var services = CreateServices(store);

        var error = await Assert.ThrowsAsync<FailedPreconditionException>(async () =>
            await services.GetRequiredService<IQuotaAccounting>().ReserveAsync(Request()));

        Assert.Equal("FAILED_PRECONDITION", error.Status);
        store.Verify(value => value.Begin(It.IsAny<System.Data.IsolationLevel>()), Times.Never);
        store.Verify(value => value.AddAccountAsync(It.IsAny<SchemataQuotaAccount>(), It.IsAny<CancellationToken>()), Times.Never);
        store.Verify(value => value.AddReservationAsync(It.IsAny<SchemataQuotaReservation>(), It.IsAny<IReadOnlyList<SchemataQuotaReservationItem>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task Reject_Black_Operation_Ids_Before_Beginning_A_Transaction(string operationId) {
        var store = new Mock<IQuotaStore>(MockBehavior.Strict);
        await using var services = CreateServices(store, new StaticScopeResolver(new("scope")));

        await Assert.ThrowsAsync<InvalidArgumentException>(async () =>
            await services.GetRequiredService<IQuotaAccounting>().ReserveAsync(Request(operationId: operationId)));

        store.Verify(value => value.Begin(It.IsAny<System.Data.IsolationLevel>()), Times.Never);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Reject_Non_Positive_Weighted_Charges_Before_Beginning_A_Transaction(long amount) {
        var store = new Mock<IQuotaStore>(MockBehavior.Strict);
        await using var services = CreateServices(store, new StaticScopeResolver(new("scope")));

        await Assert.ThrowsAsync<InvalidArgumentException>(async () =>
            await services.GetRequiredService<IQuotaAccounting>().ReserveAsync(Request(amount: amount)));

        store.Verify(value => value.Begin(It.IsAny<System.Data.IsolationLevel>()), Times.Never);
    }

    [Fact]
    public async Task Require_Concurrency_Expiry_Before_Beginning_A_Transaction() {
        var store = new Mock<IQuotaStore>(MockBehavior.Strict);
        await using var services = CreateServices(store, new StaticScopeResolver(new("scope")));
        var request = Request(algorithm: QuotaConstants.Algorithms.Concurrency, expiresAt: null);

        await Assert.ThrowsAsync<InvalidArgumentException>(async () =>
            await services.GetRequiredService<IQuotaAccounting>().ReserveAsync(request));

        store.Verify(value => value.Begin(It.IsAny<System.Data.IsolationLevel>()), Times.Never);
    }

    [Fact]
    public async Task Reject_A_Non_Cas_Store_Before_Any_Reservation_Or_Ledger_Write() {
        var account = Account();
        var store = new Mock<IQuotaStore>(MockBehavior.Strict);
        var transaction = new Mock<IUnitOfWork>();
        transaction.Setup(value => value.DisposeAsync()).Returns(ValueTask.CompletedTask);
        store.Setup(value => value.Begin(System.Data.IsolationLevel.Serializable)).Returns(transaction.Object);
        store.Setup(value => value.FindReservationAsync(new("scope"), new("subject"), "operation", It.IsAny<CancellationToken>()))
             .ReturnsAsync((SchemataQuotaReservation?)null);
        store.Setup(value => value.ListExpiredPendingAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
             .Returns(EmptyReservations);
        store.Setup(value => value.FindAccountAsync(new("scope"), new("subject"), new("calls"), "requests", 1, It.IsAny<CancellationToken>()))
             .ReturnsAsync(account);
        store.Setup(value => value.ListSegmentsAsync(account.Uid, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        store.Setup(value => value.SaveAccountAsync(account, It.IsAny<CancellationToken>()))
             .ThrowsAsync(new FailedPreconditionException());
        await using var services = CreateServices(store, new StaticScopeResolver(new("scope")));

        await Assert.ThrowsAsync<FailedPreconditionException>(async () =>
            await services.GetRequiredService<IQuotaAccounting>().ReserveAsync(Request()));

        store.Verify(value => value.AddReservationAsync(It.IsAny<SchemataQuotaReservation>(), It.IsAny<IReadOnlyList<SchemataQuotaReservationItem>>(), It.IsAny<CancellationToken>()), Times.Never);
        store.Verify(value => value.ReplaceSegmentsAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyList<SchemataQuotaSegment>>(), It.IsAny<CancellationToken>()), Times.Never);
        store.Verify(value => value.AddLedgerEntriesAsync(It.IsAny<IReadOnlyList<SchemataQuotaLedgerEntry>>(), It.IsAny<CancellationToken>()), Times.Never);
        transaction.Verify(value => value.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Enlisted_Reserve_Preserves_Operation_Error_Before_Rollback_Error() {
        var operation = new OperationCanceledException("operation cancelled");
        var cleanup = new InvalidOperationException("rollback failed");
        var store = new Mock<IQuotaStore>();
        var transaction = new Mock<IUnitOfWork>();
        store.Setup(value => value.ListExpiredPendingAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).Returns(EmptyReservations);
        store.Setup(value => value.FindReservationAsync(It.IsAny<QuotaScope>(), It.IsAny<QuotaSubject>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(operation);
        transaction.Setup(value => value.RollbackAsync(CancellationToken.None)).ThrowsAsync(cleanup);
        await using var services = CreateServices(store, new StaticScopeResolver(new("scope")));

        var error = await Assert.ThrowsAsync<AggregateException>(async () =>
            await services.GetRequiredService<IQuotaAccounting>().ReserveAsync(Request(), transaction.Object));

        Assert.Same(operation, error.InnerExceptions[0]);
        Assert.Same(cleanup, error.InnerExceptions[1]);
    }

    [Fact]
    [Trait("Layer", "Unit")]
    public async Task Owned_Reserve_Preserves_Operation_Error_Before_Disposal_Error() {
        var operation = new OperationCanceledException("reserve cancelled");
        var cleanup = new InvalidOperationException("rollback failed");
        var store = new Mock<IQuotaStore>(MockBehavior.Strict);
        var transaction = new Mock<IUnitOfWork>(MockBehavior.Strict);
        store.Setup(value => value.Begin(System.Data.IsolationLevel.Serializable)).Returns(transaction.Object);
        store.Setup(value => value.ListExpiredPendingAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .Returns(EmptyReservations);
        store.Setup(value => value.FindReservationAsync(new("scope"), new("subject"), "operation", It.IsAny<CancellationToken>()))
            .ThrowsAsync(operation);
        transaction.Setup(value => value.DisposeAsync()).Returns(() => new ValueTask(Task.FromException(cleanup)));
        await using var services = CreateServices(store, new StaticScopeResolver(new("scope")));

        var error = await Assert.ThrowsAsync<AggregateException>(async () =>
            await services.GetRequiredService<IQuotaAccounting>().ReserveAsync(Request()));

        Assert.Collection(error.InnerExceptions, first => Assert.Same(operation, first), second => Assert.Same(cleanup, second));
        transaction.Verify(value => value.DisposeAsync(), Times.Once);
        transaction.Verify(value => value.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
        store.Verify(value => value.AddReservationAsync(It.IsAny<SchemataQuotaReservation>(), It.IsAny<IReadOnlyList<SchemataQuotaReservationItem>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    [Trait("Layer", "Unit")]
    public async Task Owned_Settlement_Preserves_Aborted_Error_And_Does_Not_Read_Back_After_Disposal_Fails() {
        var operation = new AbortedException();
        var cleanup = new InvalidOperationException("rollback failed");
        var reservation = new SchemataQuotaReservation {
            Uid = Guid.NewGuid(), Scope = "scope", Subject = "subject", OperationId = "operation",
            PayloadFingerprint = "fingerprint", State = QuotaReservationState.Pending,
        };
        var store = new Mock<IQuotaStore>(MockBehavior.Strict);
        var transaction = new Mock<IUnitOfWork>(MockBehavior.Strict);
        store.Setup(value => value.Begin(System.Data.IsolationLevel.Serializable)).Returns(transaction.Object);
        store.SetupSequence(value => value.GetReservationAsync(reservation.Uid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(reservation)
            .ReturnsAsync(new SchemataQuotaReservation {
                Uid = reservation.Uid, Scope = "scope", Subject = "subject", OperationId = "operation",
                PayloadFingerprint = "fingerprint", State = QuotaReservationState.Committed,
            });
        store.Setup(value => value.SaveReservationAsync(reservation, It.IsAny<CancellationToken>())).ThrowsAsync(operation);
        transaction.Setup(value => value.DisposeAsync()).Returns(() => new ValueTask(Task.FromException(cleanup)));
        await using var services = CreateServices(store);

        var error = await Assert.ThrowsAsync<AggregateException>(async () =>
            await services.GetRequiredService<IQuotaAccounting>().CommitAsync(new(new("scope"), new("subject"), reservation.Uid)));

        Assert.Collection(error.InnerExceptions, first => Assert.Same(operation, first), second => Assert.Same(cleanup, second));
        store.Verify(value => value.GetReservationAsync(reservation.Uid, It.IsAny<CancellationToken>()), Times.Once);
        transaction.Verify(value => value.DisposeAsync(), Times.Once);
        transaction.Verify(value => value.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
        store.Verify(value => value.AddLedgerEntriesAsync(It.IsAny<IReadOnlyList<SchemataQuotaLedgerEntry>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    [Trait("Layer", "Unit")]
    public async Task Owned_Settlement_Does_Not_Recover_An_Aborted_Disposal_Error_As_A_Cas_Loss() {
        var cleanup = new AbortedException();
        var reservation = new SchemataQuotaReservation {
            Uid = Guid.NewGuid(), Scope = "scope", Subject = "subject", OperationId = "operation",
            PayloadFingerprint = "fingerprint", State = QuotaReservationState.Committed,
        };
        var store = new Mock<IQuotaStore>(MockBehavior.Strict);
        var transaction = new Mock<IUnitOfWork>(MockBehavior.Strict);
        store.Setup(value => value.Begin(System.Data.IsolationLevel.Serializable)).Returns(transaction.Object);
        store.Setup(value => value.GetReservationAsync(reservation.Uid, It.IsAny<CancellationToken>())).ReturnsAsync(reservation);
        transaction.Setup(value => value.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        transaction.Setup(value => value.DisposeAsync()).Returns(() => new ValueTask(Task.FromException(cleanup)));
        await using var services = CreateServices(store);

        var error = await Assert.ThrowsAsync<AbortedException>(async () =>
            await services.GetRequiredService<IQuotaAccounting>().CommitAsync(new(new("scope"), new("subject"), reservation.Uid)));

        Assert.Same(cleanup, error);
        store.Verify(value => value.GetReservationAsync(reservation.Uid, It.IsAny<CancellationToken>()), Times.Once);
        transaction.Verify(value => value.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        transaction.Verify(value => value.DisposeAsync(), Times.Once);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(201)]
    public async Task Reject_Invalid_Ledger_Page_Sizes(int pageSize) {
        var store = new Mock<IQuotaStore>(MockBehavior.Strict);
        await using var services = CreateServices(store);

        await Assert.ThrowsAsync<InvalidArgumentException>(async () =>
            await services.GetRequiredService<IQuotaAccounting>().ListLedgerAsync(new(new("scope"), new("subject"), null, pageSize)));

        store.Verify(value => value.ListLedgerAsync(It.IsAny<QuotaLedgerQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Resolve_Principal_Subject_From_Sub_Before_Name_Identifier() {
        var resolver = new PrincipalQuotaSubjectResolver();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "name-identifier"), new Claim("sub", "subject"),
        ], "test"));

        var subject = await resolver.ResolveAsync(principal, "subject");

        Assert.Equal(new QuotaSubject("subject"), subject);
    }

    [Fact]
    public async Task Reject_Requested_Subject_That_Differs_From_The_Authenticated_Subject() {
        var resolver = new PrincipalQuotaSubjectResolver();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "subject")], "test"));

        await Assert.ThrowsAsync<PermissionDeniedException>(async () => await resolver.ResolveAsync(principal, "other"));
    }

    [Fact]
    public async Task Reject_Unauthenticated_Principal_Subjects() {
        var resolver = new PrincipalQuotaSubjectResolver();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "subject")]));

        await Assert.ThrowsAsync<UnauthenticatedException>(async () => await resolver.ResolveAsync(principal, null));
    }

    [Fact]
    public async Task Prefer_Sub_Across_All_Authenticated_Identities() {
        var resolver = new PrincipalQuotaSubjectResolver();
        var principal = new ClaimsPrincipal(new[] {
            new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "name-identifier")], "test"),
            new ClaimsIdentity([new Claim("sub", "subject")], "test"),
        });

        var subject = await resolver.ResolveAsync(principal, null);

        Assert.Equal(new QuotaSubject("subject"), subject);
    }

    [Fact]
    public async Task Reject_An_Explicitly_Blank_Requested_Subject() {
        var resolver = new PrincipalQuotaSubjectResolver();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "subject")], "test"));

        await Assert.ThrowsAsync<PermissionDeniedException>(async () => await resolver.ResolveAsync(principal, " "));
    }

    private static ServiceProvider CreateServices(Mock<IQuotaStore> store, IQuotaScopeResolver? scopes = null) {
        var services = new ServiceCollection();
        services.AddSingleton(store.Object);
        services.AddSingleton(TimeProvider.System);
        if (scopes is not null) services.AddSingleton(scopes);
        services.AddSchemataQuotaAccounting();
        return services.BuildServiceProvider();
    }

    private static QuotaReservationRequest Request(
        string operationId = "operation",
        long amount = 1,
        string algorithm = QuotaConstants.Algorithms.FixedWindow,
        DateTimeOffset? expiresAt = null
    ) {
        return new(null, new("subject"), operationId, [
            new(new("requests", 1, algorithm, 10, TimeSpan.FromMinutes(1), 10), new("calls"), amount),
        ], expiresAt);
    }

    private static SchemataQuotaAccount Account() {
        return new() {
            Uid = Guid.NewGuid(), Scope = "scope", Subject = "subject", Metric = "calls",
            PolicyName = "requests", PolicyVersion = 1, Available = 10, WindowStart = DateTimeOffset.UtcNow,
            Timestamp = Guid.NewGuid(), CreateTime = DateTime.UtcNow, UpdateTime = DateTime.UtcNow,
        };
    }

    private static async IAsyncEnumerable<SchemataQuotaReservation> EmptyReservations() {
        await Task.CompletedTask;
        yield break;
    }

    private sealed class StaticScopeResolver(QuotaScope scope) : IQuotaScopeResolver
    {
        public ValueTask<QuotaScope> ResolveAsync(CancellationToken ct = default) {
            return ValueTask.FromResult(scope);
        }
    }
}
