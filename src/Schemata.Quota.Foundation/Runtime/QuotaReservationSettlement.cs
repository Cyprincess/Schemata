using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions.Exceptions;
using Schemata.Quota.Skeleton;
using Schemata.Quota.Skeleton.Entities;

namespace Schemata.Quota.Foundation.Runtime;

public sealed class QuotaReservationSettlement(IQuotaStore store, IServiceProvider services, TimeProvider time)
{
    public async Task<QuotaReservationView?> SettleAsync(
        Guid reservationUid,
        QuotaSettlementKind kind,
        CancellationToken ct,
        QuotaScope? expectedScope = null,
        QuotaSubject? expectedSubject = null
    ) {
        var reservation = await store.GetReservationAsync(reservationUid, ct);
        if (reservation is null) return null;
        if (expectedScope is { } scope && !string.Equals(reservation.Scope, scope.Value, StringComparison.Ordinal)
         || expectedSubject is { } subject && !string.Equals(reservation.Subject, subject.Value, StringComparison.Ordinal)) {
            throw new NotFoundException();
        }

        if (reservation.State != QuotaReservationState.Pending) return await ViewAsync(reservation, ct);
        var now = time.GetUtcNow();
        if (kind == QuotaSettlementKind.Expire && reservation.ExpireTime is { } expiry && expiry > now.UtcDateTime) {
            return await ViewAsync(reservation, ct);
        }

        var entries = new List<SchemataQuotaLedgerEntry>(reservation.Items.Count);
        foreach (var item in reservation.Items) {
            var account = await store.FindAccountAsync(new(reservation.Scope), new(reservation.Subject), new(item.Metric),
                item.PolicyName, item.PolicyVersion, ct) ?? throw new FailedPreconditionException();
            var segments = await store.ListSegmentsAsync(account.Uid, ct);
            var algorithm = services.GetRequiredKeyedService<IQuotaAlgorithm>(item.Algorithm);
            var transition = algorithm.Settle(new(item.ToPolicy(), new(item.Metric), item.Amount, kind, account, segments, now,
                item.WindowStart, item.Uid));
            account.Committed = transition.Committed;
            account.Reserved = transition.Reserved;
            account.Available = transition.Available;
            account.WindowStart = transition.WindowStart;
            await store.SaveAccountAsync(account, ct);
            await store.ReplaceSegmentsAsync(account.Uid, transition.Segments, ct);
            entries.Add(new() {
                Uid = Guid.NewGuid(), ReservationUid = reservation.Uid, AccountUid = account.Uid,
                OperationId = reservation.OperationId, Kind = kind, Amount = item.Amount, CreateTime = now.UtcDateTime,
            });
        }

        reservation.State = kind switch {
            QuotaSettlementKind.Commit => QuotaReservationState.Committed,
            QuotaSettlementKind.Release => QuotaReservationState.Released,
            _ => QuotaReservationState.Expired,
        };
        await store.SaveReservationAsync(reservation, ct);
        await store.AddLedgerEntriesAsync(entries, ct);
        return await ViewAsync(reservation, ct);
    }

    public async Task<QuotaReservationView> ViewAsync(SchemataQuotaReservation reservation, CancellationToken ct) {
        var accounts = new List<QuotaAccountView>(reservation.Items.Count);
        foreach (var item in reservation.Items) {
            var account = await store.FindAccountAsync(new(reservation.Scope), new(reservation.Subject), new(item.Metric),
                item.PolicyName, item.PolicyVersion, ct) ?? throw new FailedPreconditionException();
            accounts.Add(new(item.ToPolicy(), new(item.Metric), account.Committed, account.Reserved, account.Available));
        }

        return new(reservation.Uid, new(reservation.Scope), new(reservation.Subject), reservation.OperationId,
            reservation.State, reservation.ExpireTime is { } expiry ? new DateTimeOffset(expiry, TimeSpan.Zero) : null,
            accounts, null);
    }
}
