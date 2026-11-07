using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Exceptions;
using Schemata.Entity.Repository;
using Schemata.Quota.Skeleton;
using Schemata.Quota.Skeleton.Entities;

namespace Schemata.Quota.Foundation.Runtime;

public sealed class RepositoryQuotaStore(
    IRepository<SchemataQuotaAccount> accounts,
    IRepository<SchemataQuotaSegment> segments,
    IRepository<SchemataQuotaReservation> reservations,
    IRepository<SchemataQuotaReservationItem> items,
    IRepository<SchemataQuotaLedgerEntry> ledger) : IQuotaStore
{
    public IUnitOfWork Begin(IsolationLevel isolationLevel) {
        var transaction = accounts.Begin(isolationLevel);
        segments.Join(transaction);
        reservations.Join(transaction);
        items.Join(transaction);
        ledger.Join(transaction);
        return transaction;
    }

    public void Join(IUnitOfWork transaction) {
        accounts.Join(transaction);
        segments.Join(transaction);
        reservations.Join(transaction);
        items.Join(transaction);
        ledger.Join(transaction);
    }

    public ValueTask<SchemataQuotaAccount?> FindAccountAsync(
        QuotaScope scope,
        QuotaSubject subject,
        QuotaMetric metric,
        string policyName,
        int policyVersion,
        CancellationToken ct
    ) {
        return accounts.SingleOrDefaultAsync(query => query.Where(account =>
            account.Scope == scope.Value && account.Subject == subject.Value && account.Metric == metric.Value &&
            account.PolicyName == policyName && account.PolicyVersion == policyVersion), ct);
    }

    public async ValueTask<IReadOnlyList<SchemataQuotaSegment>> ListSegmentsAsync(Guid accountUid, CancellationToken ct) {
        var result = await segments.ListAsync(query => query.Where(segment => segment.AccountUid == accountUid), ct).ToListAsync(ct);
        return result.OrderBy(segment => segment.StartTime).ToList();
    }

    public async ValueTask<SchemataQuotaReservation?> FindReservationAsync(
        QuotaScope scope,
        QuotaSubject subject,
        string operationId,
        CancellationToken ct
    ) {
        var reservation = await reservations.SingleOrDefaultAsync(query => query.Where(row =>
            row.Scope == scope.Value && row.Subject == subject.Value && row.OperationId == operationId), ct);
        return await PopulateItemsAsync(reservation, ct);
    }

    public async ValueTask<SchemataQuotaReservation?> GetReservationAsync(Guid reservationUid, CancellationToken ct) {
        var reservation = await reservations.SingleOrDefaultAsync(query => query.Where(row => row.Uid == reservationUid), ct);
        return await PopulateItemsAsync(reservation, ct);
    }

    public async IAsyncEnumerable<SchemataQuotaReservation> ListExpiredPendingAsync(
        DateTimeOffset now,
        [EnumeratorCancellation] CancellationToken ct
    ) {
        var threshold = now.UtcDateTime;
        await foreach (var reservation in reservations.ListAsync(query => query.Where(row =>
                               row.State == QuotaReservationState.Pending && row.ExpireTime != null && row.ExpireTime <= threshold), ct)) {
            if (await PopulateItemsAsync(reservation, ct) is { } populated) yield return populated;
        }
    }

    public async IAsyncEnumerable<SchemataQuotaLedgerEntry> ListLedgerAsync(
        QuotaLedgerQuery query,
        [EnumeratorCancellation] CancellationToken ct
    ) {
        var reservationUids = await reservations.ListAsync(source => source.Where(row =>
                row.Scope == query.Scope.Value && row.Subject == query.Subject.Value).Select(row => row.Uid), ct)
            .ToListAsync(ct);
        var uids = reservationUids.ToHashSet();
        HashSet<Guid>? accountUids = null;
        if (query.Metric is { } metric) {
            accountUids = (await items.ListAsync(source => source.Where(row => uids.Contains(row.ReservationUid) && row.Metric == metric.Value)
                                                                  .Select(row => row.AccountUid), ct).ToListAsync(ct)).ToHashSet();
        }

        await foreach (var entry in ledger.ListAsync(source => source.Where(row => uids.Contains(row.ReservationUid)
                                                                        && (accountUids == null || accountUids.Contains(row.AccountUid)))
                                                                  .OrderByDescending(row => row.CreateTime).ThenBy(row => row.Uid)
                                                                  .Take(query.PageSize), ct)) {
            yield return entry;
        }
    }

    public async Task AddAccountAsync(SchemataQuotaAccount account, CancellationToken ct) {
        if (account.Uid == Guid.Empty) account.Uid = Guid.NewGuid();
        RequireApplied(await accounts.AddAsync(account, ct));
    }

    public async Task SaveAccountAsync(SchemataQuotaAccount account, CancellationToken ct) {
        RequireCas(account.Timestamp);
        RequireApplied(await accounts.UpdateAsync(account, ct));
    }

    public async Task ReplaceSegmentsAsync(
        Guid accountUid,
        IReadOnlyList<SchemataQuotaSegment> replacement,
        CancellationToken ct
    ) {
        var current = await segments.ListAsync(query => query.Where(segment => segment.AccountUid == accountUid), ct).ToListAsync(ct);
        var byUid = current.ToDictionary(segment => segment.Uid);
        var retained = replacement.Select(segment => segment.Uid).ToHashSet();
        var removed = current.Where(segment => !retained.Contains(segment.Uid)).ToList();
        if (removed.Count > 0) await segments.RemoveRangeAsync(removed, ct);

        foreach (var segment in replacement) {
            if (!byUid.TryGetValue(segment.Uid, out var existing)) {
                if (segment.Uid == Guid.Empty) segment.Uid = Guid.NewGuid();
                segment.AccountUid = accountUid;
                RequireApplied(await segments.AddAsync(segment, ct));
                continue;
            }

            existing.StartTime = segment.StartTime;
            existing.ExpireTime = segment.ExpireTime;
            existing.Committed = segment.Committed;
            existing.Reserved = segment.Reserved;
            RequireApplied(await segments.UpdateAsync(existing, ct));
        }
    }

    public async Task AddReservationAsync(
        SchemataQuotaReservation reservation,
        IReadOnlyList<SchemataQuotaReservationItem> reservationItems,
        CancellationToken ct
    ) {
        if (reservation.Uid == Guid.Empty) reservation.Uid = Guid.NewGuid();
        RequireApplied(await reservations.AddAsync(reservation, ct));
        foreach (var item in reservationItems) {
            if (item.Uid == Guid.Empty) item.Uid = Guid.NewGuid();
            item.ReservationUid = reservation.Uid;
        }

        if (reservationItems.Count > 0) await items.AddRangeAsync(reservationItems, ct);
        reservation.Items = reservationItems.ToList();
    }

    public async Task SaveReservationAsync(SchemataQuotaReservation reservation, CancellationToken ct) {
        RequireCas(reservation.Timestamp);
        RequireApplied(await reservations.UpdateAsync(reservation, ct));
    }

    public async Task AddLedgerEntriesAsync(IReadOnlyList<SchemataQuotaLedgerEntry> entries, CancellationToken ct) {
        foreach (var entry in entries) if (entry.Uid == Guid.Empty) entry.Uid = Guid.NewGuid();
        if (entries.Count > 0) await ledger.AddRangeAsync(entries, ct);
    }

    private async ValueTask<SchemataQuotaReservation?> PopulateItemsAsync(
        SchemataQuotaReservation? reservation,
        CancellationToken ct
    ) {
        if (reservation is null) return null;
        reservation.Items = await items.ListAsync(query => query.Where(row => row.ReservationUid == reservation.Uid)
                                                                 .OrderBy(row => row.PolicyName).ThenBy(row => row.Metric), ct)
            .ToListAsync(ct);
        return reservation;
    }

    private static void RequireCas(Guid timestamp) {
        if (timestamp == Guid.Empty) {
            throw new FailedPreconditionException(innerException: new InvalidOperationException(
                "Quota updates require a repository-loaded concurrency timestamp and provider CAS support."));
        }
    }

    private static void RequireApplied(MutationResult result) {
        if (result != MutationResult.Applied) {
            throw new FailedPreconditionException(innerException: new InvalidOperationException(
                "A repository advisor prevented a mandatory quota write."));
        }
    }
}
